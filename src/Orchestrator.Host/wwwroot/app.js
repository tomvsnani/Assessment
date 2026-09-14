// Dashboard for the SDLC orchestrator. Plain JS: one EventSource per run, summaries fetched on
// lifecycle events, everything else rendered from the event stream.
(() => {
  const $ = (id) => document.getElementById(id);
  const api = {
    scenarios: () => fetch('/api/scenarios').then(r => r.json()),
    baselines: () => fetch('/api/baselines').then(r => r.json()),
    workflow: (name) => fetch(`/api/workflows/${name}/graph`).then(r => r.json()),
    runs: () => fetch('/api/runs').then(r => r.json()),
    run: (id) => fetch(`/api/runs/${id}`).then(r => r.ok ? r.json() : null),
    artifact: (id, name) => fetch(`/api/runs/${id}/artifacts/${name}`).then(r => r.ok ? r.json() : null),
    audit: (id) => fetch(`/api/runs/${id}/audit`).then(r => r.ok ? r.json() : null),
    start: (body) => fetch('/api/runs', { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify(body) }).then(async r => ({ ok: r.ok, body: await r.json() })),
    approve: (id, body) => fetch(`/api/runs/${id}/approval`, { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify(body) }),
    stop: (id) => fetch(`/api/runs/${id}/stop`, { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify({ reason: 'dashboard' }) }),
  };

  const state = { runId: null, source: null, events: [], summary: null, startedAt: null, presets: [], runs: [], activity: {} };
  const LIFECYCLE = new Set(['RunStarted', 'StageScheduled', 'StageStarted', 'StageCompleted', 'StageFailed', 'StageInvalidated', 'StageAttemptFailed',
    'StageRetryScheduled', 'StageFallbackUsed', 'ArtifactProduced', 'ApprovalRequested', 'ApprovalDecided', 'ReplanTriggered',
    'CompensationRun', 'RollbackCompleted', 'SafeStopTriggered', 'RunCompleted', 'RunFailed', 'PolicyEvaluated']);
  const REFRESH_ON = new Set(['StageStarted', 'StageCompleted', 'StageFailed', 'StageInvalidated', 'ApprovalRequested', 'ApprovalDecided',
    'ReplanTriggered', 'RollbackCompleted', 'RunCompleted', 'RunFailed', 'ArtifactProduced', 'StageAttemptFailed']);

  // ---------- helpers ----------
  const esc = (s) => String(s ?? '').replace(/[&<>"]/g, c => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;' }[c]));
  const fmtSecs = (iso) => state.startedAt ? ((new Date(iso) - state.startedAt) / 1000).toFixed(1) + 's' : '';
  const banner = (text, isError) => { const b = $('banner'); b.hidden = !text; b.textContent = text || ''; b.classList.toggle('error', !!isError); };

  // Minimal Markdown: headings, fences, inline code, bold, lists, tables, paragraphs. Enough for specs and designs.
  function markdown(md) {
    const lines = md.replace(/\r/g, '').split('\n');
    let html = '', inCode = false, inList = false, inTable = false, para = [];
    const flushPara = () => { if (para.length) { html += `<p>${inline(para.join(' '))}</p>`; para = []; } };
    const closeList = () => { if (inList) { html += '</ul>'; inList = false; } };
    const closeTable = () => { if (inTable) { html += '</table>'; inTable = false; } };
    const inline = (s) => esc(s).replace(/`([^`]+)`/g, '<code>$1</code>').replace(/\*\*([^*]+)\*\*/g, '<strong>$1</strong>');
    for (const line of lines) {
      if (line.startsWith('```')) { flushPara(); closeList(); closeTable(); inCode = !inCode; html += inCode ? '<pre>' : '</pre>'; continue; }
      if (inCode) { html += esc(line) + '\n'; continue; }
      const h = /^(#{1,3})\s+(.*)/.exec(line);
      if (h) { flushPara(); closeList(); closeTable(); html += `<h${h[1].length}>${inline(h[2])}</h${h[1].length}>`; continue; }
      if (/^\s*[-*]\s+/.test(line)) { flushPara(); closeTable(); if (!inList) { html += '<ul>'; inList = true; } html += `<li>${inline(line.replace(/^\s*[-*]\s+/, ''))}</li>`; continue; }
      if (/^\s*\|/.test(line)) {
        if (/^\s*\|?\s*:?-{2,}/.test(line)) continue;
        flushPara(); closeList();
        if (!inTable) { html += '<table>'; inTable = true; }
        const cells = line.trim().replace(/^\||\|$/g, '').split('|').map(c => `<td>${inline(c.trim())}</td>`).join('');
        html += `<tr>${cells}</tr>`; continue;
      }
      if (line.trim() === '') { flushPara(); closeList(); closeTable(); continue; }
      para.push(line);
    }
    flushPara(); closeList(); closeTable();
    return html;
  }

  function renderContent(kind, content) {
    if ((kind || '').toLowerCase().includes('json') || /^[\s]*[\[{]/.test(content)) {
      try { return `<pre>${esc(JSON.stringify(JSON.parse(content), null, 2))}</pre>`; } catch { /* fall through */ }
    }
    return markdown(content);
  }

  // ---------- tabs ----------
  document.querySelectorAll('.tabs button').forEach(b => b.addEventListener('click', () => {
    document.querySelectorAll('.tabs button').forEach(x => x.classList.toggle('active', x === b));
    document.querySelectorAll('.tab').forEach(t => t.hidden = t.id !== `tab-${b.dataset.tab}`);
    if (b.dataset.tab === 'audit') loadAudit();
    if (b.dataset.tab === 'workflow') loadWorkflow();
  }));

  // ---------- run selection ----------
  async function refreshRunList() {
    const runs = await api.runs();
    state.runs = runs;
    const picker = $('runPicker');
    const current = picker.value;
    picker.innerHTML = '<option value="">— select —</option>' + runs.map(r =>
      `<option value="${esc(r.id)}">${r.status === 'running' ? '▶ ' : r.status === 'succeeded' ? '✔ ' : '✖ '}${esc(r.id)}${r.title ? ' · ' + esc(r.title) : ''}${r.mode ? ' · ' + r.mode : ''}</option>`).join('');
    picker.value = state.runId || current || '';
    updateReplayButton();
  }

  async function selectRun(id) {
    if (state.source) { state.source.close(); state.source = null; }
    state.runId = id; state.events = []; state.summary = null; state.startedAt = null; state.activity = {};
    updateReplayButton(); renderNow();
    $('timeline').innerHTML = ''; $('agents').innerHTML = ''; $('artifactList').innerHTML = ''; $('artifactView').innerHTML = '<p class="muted">Select an artifact.</p>';
    $('runPicker').value = id || '';
    if (!id) { renderSummary(null); return; }
    await refreshSummary();
    const source = new EventSource(`/api/runs/${id}/events`);
    state.source = source;
    let pending = false;
    source.onmessage = (m) => {
      const evt = JSON.parse(m.data);
      state.events.push(evt);
      if (evt.kind === 'RunStarted') state.startedAt = new Date(evt.at);
      trackActivity(evt);
      appendTimeline(evt);
      if (evt.kind === 'AgentTurn' || evt.kind === 'ToolInvoked') appendAgentTrace(evt);
      if (REFRESH_ON.has(evt.kind) && !pending) { pending = true; setTimeout(() => { pending = false; refreshSummary(); }, 250); }
    };
    source.addEventListener('end', () => { source.close(); refreshSummary(); refreshRunList(); });
    source.onerror = () => { /* the browser reconnects; duplicates are filtered by seq on the server via Last-Event-ID */ };
  }

  async function refreshSummary() {
    if (!state.runId) return;
    const summary = await api.run(state.runId);
    state.summary = summary;
    renderSummary(summary);
  }

  // ---------- live activity ----------
  // Per stage: what the agent is doing now, derived from started/finished event pairs.
  function trackActivity(e) {
    const a = state.activity;
    const d = e.data;
    switch (e.kind) {
      case 'StageStarted': a[e.stageId] = { agent: d.agent, what: 'starting', since: e.at, kind: 'busy' }; break;
      case 'ModelCallStarted': a[e.stageId] = { agent: d.agent, what: `calling the model — turn ${d.iteration} (${d.messages} messages in context)`, since: e.at, kind: 'busy' }; break;
      case 'AgentTurn': a[e.stageId] = { agent: d.agent, what: d.toolCalls > 0 ? `model asked for ${d.toolCalls} tool call(s)` : 'model answered; parsing the artifact', since: e.at, kind: 'busy' }; break;
      case 'ToolStarted': a[e.stageId] = { agent: d.agent, what: `running ${d.tool} ${d.arguments.length > 90 ? d.arguments.slice(0, 90) + '…' : d.arguments}`, since: e.at, kind: 'busy' }; break;
      case 'ToolInvoked': a[e.stageId] = { agent: d.agent, what: `${d.tool} finished (${d.durationMs} ms); back to the model`, since: e.at, kind: 'busy' }; break;
      case 'PolicyEvaluated': a[e.stageId] = { agent: (a[e.stageId] || {}).agent || '', what: `policy ${d.policy}: ${d.verdict}`, since: e.at, kind: 'busy' }; break;
      case 'ApprovalRequested': a[e.stageId] = { agent: 'you', what: `waiting for a human decision: ${d.label}`, since: e.at, kind: 'human' }; break;
      case 'ApprovalDecided': a[e.stageId] = { agent: (a[e.stageId] || {}).agent || '', what: `decision ${d.decision} recorded`, since: e.at, kind: 'busy' }; break;
      case 'StageRetryScheduled': a[e.stageId] = { agent: (a[e.stageId] || {}).agent || '', what: `retry #${d.nextAttempt} scheduled`, since: e.at, kind: 'busy' }; break;
      case 'StageCompleted': case 'StageFailed': case 'StageInvalidated': delete a[e.stageId]; break;
      case 'RunCompleted': case 'RunFailed': state.activity = {}; break;
      default: return;
    }
    renderNow();
  }

  function renderNow() {
    const rows = Object.entries(state.activity);
    const body = $('nowBody');
    if (!rows.length) { body.innerHTML = state.summary?.status === 'running' ? '<span class="spinner"></span>scheduler is dispatching the next stage…' : 'Nothing running.'; return; }
    body.innerHTML = rows.map(([stage, a]) => `<div class="now-row ${a.kind === 'human' ? 'waiting-human' : ''}" data-since="${a.since}">
      <span class="stage-tag mono">${esc(stage)}</span><span class="agent-tag">${esc(a.agent)}</span>
      <span class="what">${a.kind === 'human' ? '👤 ' : '<span class="spinner"></span>'}${esc(a.what)}</span><span class="elapsed">0s</span></div>`).join('');
  }

  setInterval(() => {
    document.querySelectorAll('.now-row').forEach(row => {
      const secs = Math.max(0, (Date.now() - new Date(row.dataset.since)) / 1000);
      row.querySelector('.elapsed').textContent = secs >= 60 ? `${Math.floor(secs / 60)}m ${Math.round(secs % 60)}s` : `${Math.round(secs)}s`;
    });
  }, 1000);

  // ---------- rendering ----------
  function renderSummary(s) {
    $('runId').textContent = s ? s.id : '';
    const st = $('status'); st.textContent = s ? s.status : 'no run'; st.className = 'badge ' + (s ? s.status : '');
    $('mode').textContent = s ? s.mode : '';
    $('baselineBadge').textContent = s ? 'baseline ' + s.baseline : '';
    $('title').textContent = s?.title ? s.title : '';
    $('outcome').textContent = s?.outcome || '';
    $('stop').disabled = !s || s.status !== 'running';
    renderGraph(s); renderMetrics(s); renderArtifacts(s); renderApproval(s); renderLineage(s); renderNow();
  }

  function renderGraph(s) {
    const g = $('graph');
    if (!s) { g.innerHTML = '<span class="muted small">Start or select a run.</span>'; return; }
    const byId = Object.fromEntries(s.stages.map(x => [x.id, x]));
    g.innerHTML = s.parallelLevels.map((level, i) => {
      const boxes = level.map(id => {
        const st = byId[id];
        const cls = st.state + (st.failedAttempts > 0 && st.state !== 'failed' ? ' retried' : '');
        const flags = [st.approval ? '👤 ' + st.approval : '', st.policies.length ? '🛡 ' + st.policies.length : '', st.rerunFrom ? '⇄ ' + st.rerunFrom : '', st.failedAttempts ? `✖${st.failedAttempts}` : ''].filter(Boolean).join(' ');
        return `<div class="stage ${cls}" title="${esc(st.policies.join(', '))}"><div class="name">${esc(st.id)}</div><div class="agent">${esc(st.agent)}</div><div class="flags">${esc(flags)}</div></div>`;
      }).join('');
      return `${i ? '<div class="arrow">→</div>' : ''}<div class="level">${boxes}</div>`;
    }).join('');
  }

  function renderMetrics(s) {
    const t = $('metrics');
    if (!s) { t.innerHTML = ''; $('fidelity').textContent = ''; return; }
    const m = s.metrics;
    const rows = [
      ['stage success', `${m.stagesSucceeded}/${m.stagesAttempted} (${Math.round(m.successRate * 100)}%)`],
      ['retries', m.retries], ['fallbacks', m.fallbacks], ['re-plans', m.replans], ['rollbacks', m.rollbacks],
      ['policy blocks', m.policyBlocks], ['human rejections/revisions', m.humanRejections],
      ['MTTR', m.meanTimeToRecover ? fmtDuration(m.meanTimeToRecover) : 'n/a'],
      ['end-to-end', fmtDuration(m.endToEnd)],
      ...Object.entries(m.stageLatency || {}).map(([k, v]) => ['  ' + k, fmtDuration(v)]),
    ];
    t.innerHTML = rows.map(([k, v]) => `<tr><td>${esc(k)}</td><td>${esc(v)}</td></tr>`).join('');
    $('fidelity').textContent = s.replay ? `replay fidelity: ${s.replay.exact} exact, ${s.replay.bySequence} by sequence` : '';
  }

  function fmtDuration(ts) {
    // .NET TimeSpan serialises as "hh:mm:ss.fffffff" or "d.hh:mm:ss"
    const m = /(?:(\d+)\.)?(\d+):(\d+):(\d+(?:\.\d+)?)/.exec(ts || '');
    if (!m) return ts;
    const secs = (+m[1] || 0) * 86400 + (+m[2]) * 3600 + (+m[3]) * 60 + parseFloat(m[4]);
    return secs >= 60 ? `${Math.floor(secs / 60)}m ${Math.round(secs % 60)}s` : `${secs.toFixed(1)}s`;
  }

  function appendTimeline(e) {
    const isTrace = e.kind === 'AgentTurn' || e.kind === 'ToolInvoked' || e.kind === 'ModelCallStarted' || e.kind === 'ToolStarted';
    if (e.kind === 'ModelCallStarted' || e.kind === 'ToolStarted') return; // shown in the "Right now" panel instead
    if (isTrace && !$('showTrace').checked) return;
    if (e.kind === 'PolicyEvaluated' && e.data.verdict === 'Pass' && !$('showPolicyPass').checked) return;
    const d = e.data; let icon = '·', cls = '', text = '';
    switch (e.kind) {
      case 'RunStarted': icon = '🚀'; text = `run started — workflow ${d.workflow} (${d.kind}), requirement ${d.requirement}`; break;
      case 'StageScheduled': return;
      case 'StageStarted': icon = '▶'; text = `started by <span class="agent-tag">${esc(d.agent)}</span>`; break;
      case 'StageCompleted': icon = '✔'; cls = 'k-ok'; text = `completed after ${d.attempts} attempt(s)${d.fallback === 'True' ? ' via fallback' : ''}`; break;
      case 'StageAttemptFailed': icon = '✖'; cls = 'k-fail'; text = `attempt ${d.attempt} failed: ${esc(d.reason)}`; break;
      case 'StageRetryScheduled': icon = '↻'; cls = 'k-warn'; text = `retry #${d.nextAttempt} in ${d.delayMs} ms`; break;
      case 'StageFallbackUsed': icon = '↷'; cls = 'k-warn'; text = `falling back from ${d.from} to ${d.to}`; break;
      case 'StageFailed': icon = '■'; cls = 'k-fail'; text = `FAILED: ${esc(d.reason)}`; break;
      case 'StageInvalidated': icon = '⟲'; cls = 'k-warn'; text = `invalidated (because of ${esc(d.because)})`; break;
      case 'ArtifactProduced': icon = '📄'; text = `artifact <b>${esc(d.name)}</b> <span class="mono muted">${d.hash}</span>${d.derivedFrom ? ' ← ' + esc(d.derivedFrom) : ''}`; break;
      case 'PolicyEvaluated': icon = '🛡'; cls = d.verdict === 'Block' ? 'k-block' : (d.verdict === 'Warn' ? 'k-warn' : 'k-ok'); text = `policy ${d.policy} [${d.phase}] ${d.verdict.toUpperCase()}${d.verdict !== 'Pass' ? ': ' + esc(d.reason) : ''}`; break;
      case 'ApprovalRequested': icon = '👤'; cls = 'k-warn'; text = `approval requested: <b>${d.label}</b> (${d.openAmbiguities} open ambiguities) — waiting for a human`; break;
      case 'ApprovalDecided': icon = '👤'; cls = d.decision === 'Approved' || d.decision === 'OptionChosen' ? 'k-ok' : 'k-fail'; text = `${d.decisionId} <b>${d.decision}</b> by ${esc(d.actor)} — ${esc(d.rationale)}`; break;
      case 'ReplanTriggered': icon = '⇄'; cls = 'k-warn'; text = `re-plan ${d.accepted === 'true' ? 'accepted' : 'refused'}: ${esc(d.cause)}${d.rerunFrom ? ' → rerun from ' + d.rerunFrom + ' (' + d.loop + ')' : ''}; invalidated [${esc(d.invalidated)}]`; break;
      case 'CompensationRun': icon = '↩'; text = `compensated: ${d.outcome}`; break;
      case 'RollbackCompleted': icon = '⏪'; cls = 'k-fail'; text = `rollback completed (${d.stagesUndone} stage(s)): ${esc(d.reason)}`; break;
      case 'SafeStopTriggered': icon = '⛔'; cls = 'k-fail'; text = `safe stop: ${esc(d.reason)}`; break;
      case 'RunCompleted': icon = '🏁'; cls = 'k-ok'; text = 'run completed'; break;
      case 'RunFailed': icon = '🏁'; cls = 'k-fail'; text = `run failed: ${esc(d.reason)}`; break;
      case 'AgentTurn': icon = '🤖'; text = `<span class="agent-tag">${esc(d.agent)}</span> turn ${d.iteration}: ${d.toolCalls > 0 ? `asked for ${d.toolCalls} tool call(s)` : 'final answer'} <span class="muted small">(${d.inputTokens}+${d.outputTokens} tok)</span>` + (d.text ? ` <details><summary class="muted small">text</summary><pre>${esc(d.text)}</pre></details>` : ''); break;
      case 'ToolInvoked': icon = '🔧'; cls = d.isError === 'true' ? 'k-fail' : ''; text = `<span class="agent-tag">${esc(d.agent)}</span> ${esc(d.tool)} <span class="mono muted small">${esc(d.arguments.length > 160 ? d.arguments.slice(0, 160) + '…' : d.arguments)}</span> <span class="muted small">${d.durationMs} ms</span> <details><summary class="muted small">result</summary><pre>${esc(d.result)}</pre></details>`; break;
      default: text = e.kind;
    }
    const li = document.createElement('li');
    li.innerHTML = `<span class="t">${fmtSecs(e.at)}</span><span class="icon">${icon}</span><span class="${cls}">${e.stageId ? `<span class="stage-tag">${esc(e.stageId)}</span>` : ''}${text}</span>`;
    const list = $('timeline'); list.appendChild(li);
    if ($('follow').checked) list.scrollTop = list.scrollHeight;
  }

  function appendAgentTrace(e) {
    const key = `${e.stageId}::${e.data.agent}`;
    let block = document.querySelector(`.agent-block[data-key="${CSS.escape(key)}"]`);
    if (!block) {
      block = document.createElement('details'); block.className = 'agent-block'; block.dataset.key = key; block.open = true;
      block.innerHTML = `<summary>${esc(e.stageId)} · <span class="agent-tag">${esc(e.data.agent)}</span> <span class="muted small counts"></span></summary>`;
      $('agents').appendChild(block);
    }
    const d = e.data, div = document.createElement('div');
    if (e.kind === 'AgentTurn') {
      div.className = 'turn';
      div.innerHTML = `<div class="head">turn ${d.iteration} · ${d.toolCalls > 0 ? d.toolCalls + ' tool call(s) requested' : 'final answer'} · ${d.inputTokens} in / ${d.outputTokens} out</div>${d.text ? `<pre>${esc(d.text)}</pre>` : ''}`;
    } else {
      div.className = 'tool' + (d.isError === 'true' ? ' err' : '');
      div.innerHTML = `🔧 <b>${esc(d.tool)}</b> <span class="mono">${esc(d.arguments.length > 200 ? d.arguments.slice(0, 200) + '…' : d.arguments)}</span> <span class="muted">${d.durationMs} ms</span><details><summary class="muted">result</summary><pre>${esc(d.result)}</pre></details>`;
    }
    block.appendChild(div);
    const turns = block.querySelectorAll('.turn').length, tools = block.querySelectorAll('.tool').length;
    block.querySelector('.counts').textContent = `${turns} turn(s), ${tools} tool call(s)`;
  }

  function renderArtifacts(s) {
    const list = $('artifactList');
    if (!s) { list.innerHTML = ''; return; }
    const active = list.querySelector('.active')?.dataset.name;
    list.innerHTML = s.artifacts.map(a => `<li data-name="${esc(a.name)}" class="${a.name === active ? 'active' : ''}"><div>${esc(a.name)}</div><div class="kind">${a.kind} · ${esc(a.producedBy)} · ${a.hash}${a.derivedFrom.length ? ' ← ' + esc(a.derivedFrom.join(', ')) : ''}</div></li>`).join('');
    list.querySelectorAll('li').forEach(li => li.addEventListener('click', async () => {
      list.querySelectorAll('li').forEach(x => x.classList.toggle('active', x === li));
      const a = await api.artifact(s.id, li.dataset.name);
      $('artifactView').innerHTML = a ? `<h1>${esc(a.name)} <span class="muted small">${a.kind} · ${a.hash}</span></h1>${renderContent(a.kind, a.content)}` : '<p class="muted">not available</p>';
    }));
  }

  function renderApproval(s) {
    const panel = $('approval');
    const p = s?.pendingApproval;
    if (!p) { panel.hidden = true; panel.innerHTML = ''; return; }
    panel.hidden = false;
    panel.innerHTML = `<h2>Human approval required: ${esc(p.label)} — stage ${esc(p.stageId)}</h2>
      <p class="small muted">The run is paused. Read the artifact(s), resolve any open ambiguities, then decide. Your decision is recorded with your name and rationale and cited by every later stage.</p>
      ${p.artifacts.map(a => `<details open><summary>${esc(a.name)} <span class="muted small">${a.kind} · ${a.hash}</span></summary><div class="doc">${renderContent(a.kind, a.content)}</div></details>`).join('')}
      ${p.openAmbiguities.map(amb => `<div class="amb"><b>${esc(amb.id)}</b> ${esc(amb.question)}
        ${amb.options.map(o => `<label><input type="radio" name="amb-${esc(amb.id)}" value="${esc(o.id)}" ${o.id === amb.recommendedOptionId ? 'checked' : ''}> <span><b>${esc(o.id)}</b> ${esc(o.summary)}${o.id === amb.recommendedOptionId ? ' <span class="muted">(recommended)</span>' : ''}<br><span class="muted small">trade-off: ${esc(o.tradeOff)}</span></span></label>`).join('')}
      </div>`).join('')}
      <textarea id="rationale" placeholder="rationale (required for revise / reject)"></textarea>
      <div class="actions">
        <button class="ok" data-kind="Approved">Approve</button>
        <button class="warn" data-kind="RevisionRequested">Send back for revision</button>
        <button class="danger" data-kind="Rejected">Reject and stop the run</button>
        <span class="muted small">acting as <b>${esc(localStorage.getItem('actor') || '')}</b> <a href="#" id="setActor">change</a></span>
      </div>`;
    panel.querySelector('#setActor').addEventListener('click', (ev) => { ev.preventDefault(); const n = prompt('Your name for the audit log', localStorage.getItem('actor') || ''); if (n) { localStorage.setItem('actor', n); renderApproval(s); } });
    panel.querySelectorAll('button[data-kind]').forEach(b => b.addEventListener('click', async () => {
      const rationale = panel.querySelector('#rationale').value.trim();
      if (b.dataset.kind !== 'Approved' && !rationale) { banner('A rationale is required when sending back or rejecting.', true); return; }
      const resolutions = {};
      p.openAmbiguities.forEach(amb => { const chosen = panel.querySelector(`input[name="amb-${amb.id}"]:checked`); if (chosen) resolutions[amb.id] = chosen.value; });
      const r = await api.approve(s.id, { kind: b.dataset.kind, rationale, actor: localStorage.getItem('actor') || null, ambiguityResolutions: resolutions });
      banner(r.ok ? '' : `Decision not accepted (${r.status}).`, !r.ok);
      if (r.ok) { panel.hidden = true; refreshSummary(); }
    }));
  }

  function renderLineage(s) {
    const el = $('lineage');
    if (!s) { el.innerHTML = ''; return; }
    const arts = s.artifacts.map(a => `<li><b>${esc(a.name)}</b> <span class="muted">(${esc(a.producedBy)})</span>${a.derivedFrom.length ? ' ← ' + a.derivedFrom.map(esc).join(', ') : ' <span class="muted">— root</span>'}</li>`).join('');
    const decisions = state.events.filter(e => e.kind === 'ApprovalDecided').map(e => `<li><b>${e.data.decisionId}</b> ${e.data.decision} by ${esc(e.data.actor)} at <span class="mono">${esc(e.stageId)}</span> cites [${esc(e.data.cites)}] — ${esc(e.data.rationale)}</li>`).join('');
    el.innerHTML = `<h2>Artifacts and what they derive from</h2><ul>${arts || '<li class="muted">none yet</li>'}</ul><h2>Decisions and what they cite</h2><ul>${decisions || '<li class="muted">none yet</li>'}</ul><h2>Mermaid</h2><pre>${esc(s.lineageMermaid)}</pre>`;
  }

  async function loadAudit() {
    const el = $('audit');
    if (!state.runId) { el.innerHTML = '<p class="muted">No run selected.</p>'; return; }
    const a = await api.audit(state.runId);
    if (!a) { el.innerHTML = '<p class="muted">No audit file yet (written as the run progresses).</p>'; return; }
    el.innerHTML = `<p>${a.intact ? '<span class="k-ok">✔ hash chain intact</span>' : `<span class="k-fail">✖ chain broken at seq ${a.brokenAt}</span>`} — ${a.entries.length} entries, each carrying the hash of the previous one.</p>` +
      a.entries.map(e => `<div class="audit-row ${a.brokenAt && e.seq >= a.brokenAt ? 'broken' : ''}"><span class="mono">${e.seq}</span><span class="mono muted">${new Date(e.at).toLocaleTimeString()}</span><span>${esc(e.stageId || '')}</span><span>${esc(e.actor)}</span><span><b>${esc(e.action)}</b> ${esc(e.outcome)} — ${esc(e.detail)}</span></div>`).join('');
  }

  async function loadWorkflow() {
    const w = await api.workflow(state.summary?.workflow || 'sdlc');
    $('workflow').innerHTML = `<h1>${esc(w.name)} <span class="muted small">max parallel ${w.maxParallelStages}</span></h1>
      <p class="muted small">Every run, preset or ad hoc, follows this graph. It is defined in <code>workflows/${esc(w.name)}.yaml</code>.</p>
      <h2>Stages</h2><table><tr><th>stage</th><th>agent</th><th>depends on</th><th>entry gate</th><th>exit gate</th><th>retry / fallback</th><th>on failure</th></tr>
      ${w.stages.map(s => `<tr><td><b>${esc(s.id)}</b></td><td>${esc(s.agent)}</td><td>${esc(s.dependsOn.join(', '))}</td><td>${gate(s.entry)}</td><td>${gate(s.exit)}</td><td>${s.retry.maxAttempts} attempt(s)${s.fallbackAgent ? ', fallback ' + esc(s.fallbackAgent) : ''}</td><td>${s.onFailure.rerunFrom ? 'rerun from ' + esc(s.onFailure.rerunFrom) + ' ×' + s.onFailure.maxLoops : 'stop run'}</td></tr>`).join('')}</table><h2>YAML</h2><pre>${esc(w.yaml)}</pre>`;
    function gate(g) { return [g.requiredArtifacts.length ? 'artifacts: ' + esc(g.requiredArtifacts.join(', ')) : '', g.policies.length ? 'policies: ' + esc(g.policies.join(', ')) : '', g.approval ? '<b>👤 ' + esc(g.approval) + '</b>' : ''].filter(Boolean).join('<br>') || '<span class="muted">open</span>'; }
  }

  // ---------- composer ----------
  function openComposer() { $('composer').hidden = false; $('composer').scrollIntoView({ behavior: 'smooth' }); }
  function applyPreset(name) {
    const p = state.presets.find(x => x.name === name);
    if (!p) { $('composeHint').textContent = 'Ad-hoc requirement: runs live and records into its own run directory.'; return; }
    $('reqTitle').value = p.requirement.title; $('reqText').value = p.requirement.text; $('baseline').value = p.baseline;
    $('live').checked = !p.hasRecording; $('approver').value = $('live').checked ? 'web' : 'replay';
    $('composeHint').textContent = p.hasRecording ? 'This preset has committed recordings: untick "live" to replay without a key. Edit the text and it becomes an ad-hoc run.' : 'No recording yet for this preset; it will run live.';
  }
  function updateReplayButton() {
    const r = state.runs.find(x => x.id === state.runId);
    $('replayRun').disabled = !(r && r.status !== 'running' && r.replayable);
  }
  $('newRun').addEventListener('click', openComposer);
  $('cancelCompose').addEventListener('click', () => { $('composer').hidden = true; });
  $('preset').addEventListener('change', (e) => applyPreset(e.target.value));
  $('live').addEventListener('change', () => { $('approver').value = $('live').checked ? 'web' : 'replay'; });
  $('start').addEventListener('click', async () => {
    const presetName = $('preset').value;
    const p = state.presets.find(x => x.name === presetName);
    const unchanged = p && $('reqText').value.trim() === p.requirement.text.trim() && $('baseline').value === p.baseline;
    const body = unchanged
      ? { scenario: presetName, live: $('live').checked, approver: $('approver').value }
      : { requirement: { title: $('reqTitle').value, text: $('reqText').value }, baseline: $('baseline').value, live: true, approver: $('approver').value === 'replay' ? 'web' : $('approver').value };
    if (!unchanged && !$('reqText').value.trim()) { banner('Write a requirement first.', true); return; }
    const r = await api.start(body);
    if (!r.ok) { banner(r.body.error || 'could not start', true); return; }
    banner(''); $('composer').hidden = true;
    await refreshRunList();
    await selectRun(r.body.id);
  });
  $('replayRun').addEventListener('click', async () => {
    const r = await api.start({ replayOf: state.runId, approver: 'replay' });
    if (!r.ok) { banner(r.body.error || 'could not replay', true); return; }
    await refreshRunList(); await selectRun(r.body.id);
  });
  $('stop').addEventListener('click', async () => { if (state.runId && confirm('Trigger a safe stop? Running agents are cancelled and completed stages are rolled back.')) await api.stop(state.runId); });
  $('runPicker').addEventListener('change', (e) => selectRun(e.target.value));
  $('showTrace').addEventListener('change', () => { $('timeline').innerHTML = ''; state.events.forEach(appendTimeline); });
  $('showPolicyPass').addEventListener('change', () => { $('timeline').innerHTML = ''; state.events.forEach(appendTimeline); });

  // ---------- boot ----------
  (async () => {
    const [presets, baselines] = await Promise.all([api.scenarios(), api.baselines()]);
    state.presets = presets;
    $('preset').innerHTML = '<option value="">— write your own —</option>' + presets.map(p => `<option value="${esc(p.name)}">${esc(p.name)}${p.hasRecording ? ' (recorded)' : ''}</option>`).join('');
    $('baseline').innerHTML = baselines.map(b => `<option value="${esc(b.id)}">${esc(b.id)} — ${esc(b.description)}</option>`).join('');
    if (!localStorage.getItem('actor')) localStorage.setItem('actor', 'dashboard-user');
    await refreshRunList();
    renderSummary(null);
    const running = state.runs.find(r => r.status === 'running');
    if (running) selectRun(running.id); else if (!state.runs.length) openComposer();
  })();
})();
