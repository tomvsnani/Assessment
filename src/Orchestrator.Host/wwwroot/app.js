// Dashboard for the SDLC orchestrator. Two screens: Start (choose/write a requirement, run it)
// and Run (watch one run: stage graph, live activity, approvals, artifacts, audit). Plain JS.
(() => {
  const $ = (id) => document.getElementById(id);
  const json = (r) => r.ok ? r.json() : null;
  const post = (url, body) => fetch(url, { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify(body) });
  const api = {
    scenarios: () => fetch('/api/scenarios').then(r => r.json()),
    baselines: () => fetch('/api/baselines').then(r => r.json()),
    providers: () => fetch('/api/providers').then(r => r.json()),
    workflow: (name) => fetch(`/api/workflows/${name}/graph`).then(r => r.json()),
    runs: () => fetch('/api/runs').then(r => r.json()),
    run: (id) => fetch(`/api/runs/${id}`).then(json),
    artifact: (id, name) => fetch(`/api/runs/${id}/artifacts/${name}`).then(json),
    audit: (id) => fetch(`/api/runs/${id}/audit`).then(json),
    start: (body) => post('/api/runs', body).then(async r => ({ ok: r.ok, body: await r.json() })),
    approve: (id, body) => post(`/api/runs/${id}/approval`, body),
    stop: (id) => post(`/api/runs/${id}/stop`, { reason: 'dashboard' }),
    delete: (id) => fetch(`/api/runs/${id}`, { method: 'DELETE' }),
  };

  const state = { runId: null, source: null, events: [], summary: null, startedAt: null, presets: [], runs: [], providers: null, activity: {} };
  const REFRESH_ON = new Set(['StageStarted', 'StageCompleted', 'StageFailed', 'StageInvalidated', 'ApprovalRequested', 'ApprovalDecided',
    'ReplanTriggered', 'RollbackCompleted', 'RunCompleted', 'RunFailed', 'ArtifactProduced', 'StageAttemptFailed']);
  const BASELINE_LABELS = {
    'scaffold': 'Empty project — build configuration only (greenfield)',
    'git:v1-legacy': 'Existing shortener v1 — synchronous click counting on the redirect path (brownfield)',
    'git:HEAD': 'The repository exactly as it is now',
  };
  const SCENARIO_BLURBS = {
    greenfield: 'Build the URL shortener from nothing: API, redirects, stats, tests, OpenAPI.',
    brownfield: 'Modernize the existing shortener: move click counting off the redirect path with an outbox and a consumer.',
    ambiguous: 'A vague compliance request against the existing shortener. The agents must surface what needs deciding.',
  };

  // ---------- helpers ----------
  const esc = (s) => String(s ?? '').replace(/[&<>"]/g, c => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;' }[c]));
  const fmtSecs = (iso) => state.startedAt ? ((new Date(iso) - state.startedAt) / 1000).toFixed(1) + 's' : '';
  const banner = (text, isError) => { const b = $('banner'); b.hidden = !text; b.textContent = text || ''; b.classList.toggle('error', !!isError); };
  const baselineLabel = (id) => BASELINE_LABELS[id] || (id.startsWith('git:') ? `Code at tag ${id.slice(4)}` : id);

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
        html += `<tr>${line.trim().replace(/^\||\|$/g, '').split('|').map(c => `<td>${inline(c.trim())}</td>`).join('')}</tr>`; continue;
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

  // ---------- screens ----------
  function showStart() {
    if (state.source) { state.source.close(); state.source = null; }
    state.runId = null;
    $('view-run').hidden = true; $('view-start').hidden = false; $('runControls').hidden = true;
    location.hash = '';
    refreshRunTable();
  }

  function showRun(id) {
    $('view-start').hidden = true; $('view-run').hidden = false; $('runControls').hidden = false;
    location.hash = 'run/' + id;
    selectRun(id);
  }

  $('home').addEventListener('click', (e) => { e.preventDefault(); showStart(); });
  $('back').addEventListener('click', (e) => { e.preventDefault(); showStart(); });

  // ---------- start screen ----------
  function currentPreset() { return state.presets.find(p => p.name === $('scenarioCards').dataset.selected); }

  function renderScenarioCards() {
    const selected = $('scenarioCards').dataset.selected || '';
    $('scenarioCards').innerHTML = state.presets.map(p => `
      <button class="scenario ${p.name === selected ? 'active' : ''}" data-name="${esc(p.name)}">
        <div class="scenario-name">${esc(p.name)}</div>
        <div class="muted small">${esc(SCENARIO_BLURBS[p.name] || p.requirement.title)}</div>
        <div class="scenario-meta small">${p.hasRecording ? '<span class="k-ok">● recording available</span>' : '<span class="muted">○ not recorded yet</span>'} · starts from ${esc(baselineLabel(p.baseline).split(' — ')[0])}</div>
      </button>`).join('') + `
      <button class="scenario ${selected === '' ? 'active' : ''}" data-name="">
        <div class="scenario-name">write your own</div>
        <div class="muted small">Any requirement, against any starting code. Runs live and is recorded under its own id.</div>
        <div class="scenario-meta small muted">edit the box below</div>
      </button>`;
    $('scenarioCards').querySelectorAll('.scenario').forEach(b => b.addEventListener('click', () => choosePreset(b.dataset.name)));
  }

  function choosePreset(name) {
    $('scenarioCards').dataset.selected = name;
    const p = state.presets.find(x => x.name === name);
    if (p) {
      $('reqTitle').value = p.requirement.title; $('reqText').value = p.requirement.text; $('baseline').value = p.baseline;
    } else {
      $('reqTitle').value = ''; $('reqText').value = ''; $('baseline').value = 'scaffold';
    }
    renderScenarioCards();
    updateMode();
  }

  // Which of live / replay is possible right now, and what the Start button will do.
  function updateMode() {
    const p = currentPreset();
    const unchanged = !!p && $('reqText').value.trim() === p.requirement.text.trim() && $('baseline').value === p.baseline;
    const canReplay = unchanged && p.hasRecording;
    const replayInput = document.querySelector('input[name="mode"][value="replay"]');
    replayInput.disabled = !canReplay;
    $('replayOption').classList.toggle('disabled', !canReplay);
    $('replayHint').textContent = !p ? 'Only scenarios with a committed recording can be replayed; your own requirement runs live.'
      : !p.hasRecording ? `"${p.name}" has not been run live yet, so there is nothing to replay.`
      : !unchanged ? `You edited the "${p.name}" text, so this is a new requirement and must run live.`
      : 'Re-plays the committed model exchanges and the recorded human decisions. No key needed.';
    if (!canReplay) document.querySelector('input[name="mode"][value="live"]').checked = true;
    $('reqState').textContent = !p ? '(your own requirement)' : unchanged ? `(scenario "${p.name}", unchanged)` : `(scenario "${p.name}", edited → a new ad-hoc requirement)`;
    // A scenario fixes its starting code; only a hand-written requirement chooses one.
    $('baselineRow').hidden = !!p;
    $('baselineFixed').hidden = !p;
    $('baselineFixed').textContent = p ? `Starting code (set by the scenario): ${baselineLabel(p.baseline)}` : '';
    const live = document.querySelector('input[name="mode"]:checked').value === 'live';
    const provider = state.providers?.providers.find(x => x.id === $('provider').value);
    $('composeHint').textContent = live
      ? (provider?.hasKey ? `Will call ${provider.id} (${$('model').value.trim() || provider.defaultModel}); you approve at three gates.` : `No key for ${$('provider').value} in the host's environment — set ${provider?.keyVariable || 'the key'} and restart the host.`)
      : 'Will replay without calling any model.';
    $('start').textContent = live ? 'Start live run' : 'Replay recording';
  }
  ['reqText', 'reqTitle', 'model'].forEach(id => $(id).addEventListener('input', updateMode));
  ['baseline', 'provider'].forEach(id => $(id).addEventListener('change', updateMode));
  document.querySelectorAll('input[name="mode"]').forEach(r => r.addEventListener('change', updateMode));

  $('start').addEventListener('click', async () => {
    const p = currentPreset();
    const live = document.querySelector('input[name="mode"]:checked').value === 'live';
    const unchanged = !!p && $('reqText').value.trim() === p.requirement.text.trim() && $('baseline').value === p.baseline;
    if (!$('reqText').value.trim()) { banner('Write a requirement first.', true); return; }
    const llm = live ? { provider: $('provider').value || null, model: $('model').value.trim() || null } : {};
    const body = unchanged
      ? { scenario: p.name, live, approver: live ? $('approver').value : 'replay', ...llm }
      : { requirement: { title: $('reqTitle').value, text: $('reqText').value }, baseline: $('baseline').value, live: true, approver: $('approver').value, ...llm };
    const r = await api.start(body);
    if (!r.ok) { banner(r.body.error || 'could not start', true); return; }
    banner('');
    showRun(r.body.id);
  });

  async function refreshRunTable() {
    state.runs = await api.runs();
    const rows = state.runs.map(r => `<tr class="${r.status}">
      <td>${r.status === 'running' ? '<span class="spinner"></span>running' : r.status === 'succeeded' ? '<span class="k-ok">✔ succeeded</span>' : '<span class="k-fail">✖ ' + esc(r.status) + '</span>'}</td>
      <td><b>${esc(r.name)}</b> <span class="muted small">${esc(r.title || '')}</span></td>
      <td class="muted small">${new Date(r.startedAt).toLocaleString()}</td>
      <td class="muted small">${esc(r.mode || '')}</td>
      <td class="row-actions"><a href="#run/${esc(r.id)}" data-open="${esc(r.id)}">open</a>
        ${r.status !== 'running' && r.replayable ? `<a href="#" data-replay="${esc(r.id)}">replay</a>` : ''}
        ${r.status !== 'running' ? `<a href="#" data-delete="${esc(r.id)}" class="k-fail">delete</a>` : ''}</td>
    </tr>`).join('');
    $('runTable').innerHTML = rows ? `<tr><th>status</th><th>run</th><th>started</th><th>mode</th><th></th></tr>${rows}` : '<tr><td class="muted">No runs yet.</td></tr>';
    $('runTable').querySelectorAll('[data-open]').forEach(a => a.addEventListener('click', (e) => { e.preventDefault(); showRun(a.dataset.open); }));
    $('runTable').querySelectorAll('[data-replay]').forEach(a => a.addEventListener('click', async (e) => {
      e.preventDefault();
      const r = await api.start({ replayOf: a.dataset.replay, approver: 'replay' });
      if (!r.ok) { banner(r.body.error || 'could not replay', true); return; }
      showRun(r.body.id);
    }));
    $('runTable').querySelectorAll('[data-delete]').forEach(a => a.addEventListener('click', async (e) => {
      e.preventDefault();
      if (confirm(`Delete run ${a.dataset.delete} and everything it recorded?`)) { await api.delete(a.dataset.delete); refreshRunTable(); }
    }));
    $('deleteFailed').disabled = !state.runs.some(r => r.status === 'failed' || r.status === 'incomplete');
  }
  $('deleteFailed').addEventListener('click', async () => {
    const failed = state.runs.filter(r => r.status === 'failed' || r.status === 'incomplete');
    if (failed.length && confirm(`Delete ${failed.length} failed run(s)?`)) { await Promise.all(failed.map(r => api.delete(r.id))); refreshRunTable(); }
  });

  // ---------- run screen: selection + stream ----------
  async function selectRun(id) {
    if (state.source) { state.source.close(); state.source = null; }
    state.runId = id; state.events = []; state.summary = null; state.startedAt = null; state.activity = {};
    $('timeline').innerHTML = ''; $('agents').innerHTML = ''; $('artifactList').innerHTML = ''; $('artifactView').innerHTML = '<p class="muted">Select an artifact.</p>';
    renderNow();
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
    source.addEventListener('end', () => { source.close(); refreshSummary(); });
  }

  async function refreshSummary() {
    if (!state.runId) return;
    state.summary = await api.run(state.runId);
    renderSummary(state.summary);
  }

  // ---------- run screen: rendering ----------
  function renderSummary(s) {
    $('runId').textContent = s ? s.id : '';
    const st = $('status'); st.textContent = s ? s.status : 'no run'; st.className = 'badge ' + (s ? s.status : '');
    $('mode').textContent = s ? s.mode : '';
    $('baselineBadge').textContent = s ? baselineLabel(s.baseline).split(' — ')[0] : '';
    $('title').textContent = s?.title || '';
    $('outcome').textContent = s?.outcome || '';
    $('stop').disabled = !s || s.status !== 'running';
    renderGraph(s); renderMetrics(s); renderArtifacts(s); renderApproval(s); renderLineage(s); renderNow();
  }

  function renderGraph(s) {
    const g = $('graph');
    if (!s) { g.innerHTML = ''; return; }
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

  function fmtDuration(ts) {
    const m = /(?:(\d+)\.)?(\d+):(\d+):(\d+(?:\.\d+)?)/.exec(ts || '');
    if (!m) return ts;
    const secs = (+m[1] || 0) * 86400 + (+m[2]) * 3600 + (+m[3]) * 60 + parseFloat(m[4]);
    return secs >= 60 ? `${Math.floor(secs / 60)}m ${Math.round(secs % 60)}s` : `${secs.toFixed(1)}s`;
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

  // Per stage: what the agent is doing now, derived from started/finished event pairs.
  function trackActivity(e) {
    const a = state.activity, d = e.data;
    switch (e.kind) {
      case 'StageStarted': a[e.stageId] = { agent: d.agent, what: 'starting', since: e.at, kind: 'busy' }; break;
      case 'ModelCallStarted': a[e.stageId] = { agent: d.agent, what: `calling the model — turn ${d.iteration} (${d.messages} messages in context)`, since: e.at, kind: 'busy' }; break;
      case 'ProviderRetry': a[e.stageId] = { agent: d.agent, what: `provider ${d.status === '429' ? 'rate-limited (429)' : 'error ' + d.status}; waiting ${Math.round(d.delayMs / 1000)}s before attempt ${+d.attempt + 1}/${d.maxAttempts}`, since: e.at, kind: 'retry' }; break;
      case 'AgentTurn': a[e.stageId] = { agent: d.agent, what: d.toolCalls > 0 ? `model asked for ${d.toolCalls} tool call(s)` : 'model answered; parsing the artifact', since: e.at, kind: 'busy' }; break;
      case 'ToolStarted': a[e.stageId] = { agent: d.agent, what: `running ${d.tool} ${d.arguments.length > 90 ? d.arguments.slice(0, 90) + '…' : d.arguments}`, since: e.at, kind: 'busy' }; break;
      case 'ToolInvoked': a[e.stageId] = { agent: d.agent, what: `${d.tool} finished (${d.durationMs} ms); back to the model`, since: e.at, kind: 'busy' }; break;
      case 'PolicyEvaluated': a[e.stageId] = { agent: (a[e.stageId] || {}).agent || '', what: `policy ${d.policy}: ${d.verdict}`, since: e.at, kind: 'busy' }; break;
      case 'ApprovalRequested': a[e.stageId] = { agent: 'you', what: `waiting for your decision: ${d.label}`, since: e.at, kind: 'human' }; break;
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
      <span class="what">${a.kind === 'human' ? '👤 ' : a.kind === 'retry' ? '⏳ ' : '<span class="spinner"></span>'}${esc(a.what)}</span><span class="elapsed">0s</span></div>`).join('');
  }
  setInterval(() => {
    document.querySelectorAll('.now-row').forEach(row => {
      const secs = Math.max(0, (Date.now() - new Date(row.dataset.since)) / 1000);
      row.querySelector('.elapsed').textContent = secs >= 60 ? `${Math.floor(secs / 60)}m ${Math.round(secs % 60)}s` : `${Math.round(secs)}s`;
    });
  }, 1000);

  function appendTimeline(e) {
    if (e.kind === 'ModelCallStarted' || e.kind === 'ToolStarted' || e.kind === 'StageScheduled') return;
    const isTrace = e.kind === 'AgentTurn' || e.kind === 'ToolInvoked' || e.kind === 'ProviderRetry';
    if (isTrace && !$('showTrace').checked) return;
    if (e.kind === 'PolicyEvaluated' && e.data.verdict === 'Pass' && !$('showPolicyPass').checked) return;
    const d = e.data; let icon = '·', cls = '', text = '';
    switch (e.kind) {
      case 'RunStarted': icon = '🚀'; text = `run started — workflow ${d.workflow} (${d.kind}), requirement ${d.requirement}`; break;
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
      case 'ProviderRetry': icon = '⏳'; cls = 'k-warn'; text = `<span class="agent-tag">${esc(d.agent)}</span> provider returned ${d.status}; retry ${+d.attempt + 1}/${d.maxAttempts} in ${Math.round(d.delayMs / 1000)}s <details><summary class="muted small">detail</summary><pre>${esc(d.detail)}</pre></details>`; break;
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
    block.querySelector('.counts').textContent = `${block.querySelectorAll('.turn').length} turn(s), ${block.querySelectorAll('.tool').length} tool call(s)`;
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
    panel.innerHTML = `<h2>Your decision is needed: ${esc(p.label)} <span class="muted small">stage ${esc(p.stageId)}</span></h2>
      <p class="small muted">The run is paused. Read the artifact(s), answer any open questions, then decide. Your decision is recorded with your name and rationale and cited by every later stage.</p>
      ${p.artifacts.map(a => `<details open><summary>${esc(a.name)} <span class="muted small">${a.kind} · ${a.hash}</span></summary><div class="doc">${renderContent(a.kind, a.content)}</div></details>`).join('')}
      ${p.openAmbiguities.map(amb => `<div class="amb"><b>${esc(amb.id)}</b> ${esc(amb.question)}
        ${amb.options.map(o => `<label><input type="radio" name="amb-${esc(amb.id)}" value="${esc(o.id)}" ${o.id === amb.recommendedOptionId ? 'checked' : ''}> <span><b>${esc(o.id)}</b> ${esc(o.summary)}${o.id === amb.recommendedOptionId ? ' <span class="muted">(recommended)</span>' : ''}<br><span class="muted small">trade-off: ${esc(o.tradeOff)}</span></span></label>`).join('')}
      </div>`).join('')}
      <textarea id="rationale" placeholder="rationale (required for revise / reject)"></textarea>
      <div class="actions">
        <button class="ok" data-kind="Approved">Approve — continue</button>
        <button class="warn" data-kind="RevisionRequested">Send back — same agent redoes this stage with my notes</button>
        <button class="danger" data-kind="Rejected">Reject — stop the run</button>
        <span class="muted small">deciding as <b>${esc(localStorage.getItem('actor') || '')}</b> <a href="#" id="setActor">change</a></span>
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

  document.querySelectorAll('.tabs button').forEach(b => b.addEventListener('click', () => {
    document.querySelectorAll('.tabs button').forEach(x => x.classList.toggle('active', x === b));
    document.querySelectorAll('.tab').forEach(t => t.hidden = t.id !== `tab-${b.dataset.tab}`);
    if (b.dataset.tab === 'audit') loadAudit();
    if (b.dataset.tab === 'workflow') loadWorkflow();
  }));
  $('stop').addEventListener('click', async () => { if (state.runId && confirm('Safe stop? Running agents are cancelled and completed stages are rolled back. To send work back to an agent, use the approval panel instead.')) await api.stop(state.runId); });
  $('showTrace').addEventListener('change', () => { $('timeline').innerHTML = ''; state.events.forEach(appendTimeline); });
  $('showPolicyPass').addEventListener('change', () => { $('timeline').innerHTML = ''; state.events.forEach(appendTimeline); });

  // ---------- boot ----------
  (async () => {
    const [presets, baselines, providers] = await Promise.all([api.scenarios(), api.baselines(), api.providers()]);
    state.presets = presets; state.providers = providers;
    $('baseline').innerHTML = baselines.map(b => `<option value="${esc(b.id)}">${esc(baselineLabel(b.id))}</option>`).join('');
    $('provider').innerHTML = providers.providers.map(p => `<option value="${esc(p.id)}" ${p.hasKey ? '' : 'disabled'}>${esc(p.id)} ${p.hasKey ? '(key present)' : '(no ' + esc(p.keyVariable) + ')'}</option>`).join('');
    $('provider').value = providers.default;
    $('provider').addEventListener('change', () => { const p = providers.providers.find(x => x.id === $('provider').value); $('model').placeholder = p ? p.defaultModel : 'provider default'; });
    $('provider').dispatchEvent(new Event('change'));
    $('model').value = localStorage.getItem('model') || '';
    $('model').addEventListener('input', () => localStorage.setItem('model', $('model').value.trim()));
    if (!localStorage.getItem('actor')) localStorage.setItem('actor', 'dashboard-user');
    choosePreset(presets.find(p => p.name === 'greenfield')?.name ?? presets[0]?.name ?? '');
    const runs = await api.runs();
    const hash = /^#run\/(.+)$/.exec(location.hash);
    const running = runs.find(r => r.status === 'running');
    if (hash) showRun(hash[1]); else if (running) showRun(running.id); else showStart();
  })();
})();
