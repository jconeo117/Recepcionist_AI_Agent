using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.Tokens;
using ReceptionistAgent.Core.Tenant;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;

namespace ReceptionistAgent.Api.Controllers;

[ApiController]
[Route("api/tenant/auth")]
public class TenantAuthController : ControllerBase
{
    private readonly ITenantResolver _tenantResolver;
    private readonly IConfiguration _config;
    private readonly IWebHostEnvironment _env;

    public TenantAuthController(ITenantResolver tenantResolver, IConfiguration config, IWebHostEnvironment env)
    {
        _tenantResolver = tenantResolver;
        _config = config;
        _env = env;
    }

    [HttpPost("login")]
    public async Task<IActionResult> Login([FromBody] LoginRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Username) || string.IsNullOrWhiteSpace(request.Password))
            return BadRequest("Username and Password are required.");

        var tenant = await _tenantResolver.AuthenticateAsync(request.Username, request.Password);
        if (tenant == null)
            return Unauthorized("Invalid username or password.");

        var secretKey = _config["Jwt:Key"]
            ?? throw new InvalidOperationException("La configuración 'Jwt:Key' es requerida.");
        var issuer = _config["Jwt:Issuer"] ?? "ReceptionistAI";
        var audience = _config["Jwt:Audience"] ?? "ReceptionistAI_ClientDashboard";

        var tokenHandler = new JwtSecurityTokenHandler();
        var key = Encoding.ASCII.GetBytes(secretKey);
        var tokenDescriptor = new SecurityTokenDescriptor
        {
            Subject = new ClaimsIdentity(new[]
            {
                new Claim(ClaimTypes.NameIdentifier, tenant.TenantId),
                new Claim(ClaimTypes.Name, tenant.Username ?? tenant.TenantId),
                new Claim("BusinessName", tenant.BusinessName),
                new Claim("tenant_id", tenant.TenantId)
            }),
            Expires = DateTime.UtcNow.AddDays(7),
            Issuer = issuer,
            Audience = audience,
            SigningCredentials = new SigningCredentials(new SymmetricSecurityKey(key), SecurityAlgorithms.HmacSha256Signature)
        };
        var token = tokenHandler.CreateToken(tokenDescriptor);
        var tokenString = tokenHandler.WriteToken(token);

        // Emit httpOnly secure cookie
        Response.Cookies.Append("auth_token", tokenString, new CookieOptions
        {
            HttpOnly = true,
            Secure = !_env.IsDevelopment(),
            SameSite = SameSiteMode.Strict,
            Expires = DateTimeOffset.UtcNow.AddDays(7),
            Path = "/"
        });

        return Ok(new
        {
            TenantId = tenant.TenantId,
            BusinessName = tenant.BusinessName
        });
    }

    [HttpPost("logout")]
    public IActionResult Logout()
    {
        Response.Cookies.Delete("auth_token");
        return Ok(new { success = true });
    }

    public class LoginRequest
    {
        public string Username { get; set; } = string.Empty;
        public string Password { get; set; } = string.Empty;
    }
}
