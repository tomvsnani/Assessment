using System.ComponentModel.DataAnnotations;

namespace Shortener.Api.Models;

public record CreateShortLinkRequest([Required] string Url);
