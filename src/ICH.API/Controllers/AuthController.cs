using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using ICH.Infrastructure.Auth;
using ICH.Domain.Interfaces;
using ICH.Domain.Entities;
using ICH.Shared.DTOs;

namespace ICH.API.Controllers;

[ApiController]
[Route("api/[controller]")]
public class AuthController : ControllerBase
{
    private readonly AuthService _authService;
    private readonly IUserRepository _userRepository;
    private readonly ILogger<AuthController> _logger;

    public AuthController(
        AuthService authService,
        IUserRepository userRepository,
        ILogger<AuthController> logger)
    {
        _authService = authService;
        _userRepository = userRepository;
        _logger = logger;
    }

    /// <summary>
    /// Login with email and password.
    /// </summary>
    [HttpPost("login")]
    public async Task<ActionResult<LoginResponse>> Login([FromBody] LoginRequest request, CancellationToken ct)
    {
        var response = await _authService.AuthenticateAsync(request, ct);
        if (response == null)
            return Unauthorized(new { message = "Invalid email or password" });

        _logger.LogInformation("User logged in: {Email}", request.Email);
        return Ok(response);
    }

    /// <summary>
    /// Register a new user.
    /// </summary>
    [HttpPost("register")]
    public async Task<ActionResult<LoginResponse>> Register([FromBody] RegisterRequest request, CancellationToken ct)
    {
        var existing = await _userRepository.GetByEmailAsync(request.Email, ct);
        if (existing != null)
            return Conflict(new { message = "Email is already registered" });

        var user = new User
        {
            Email = request.Email,
            DisplayName = request.DisplayName,
            PasswordHash = AuthService.HashPassword(request.Password),
            PreferredLanguage = request.PreferredLanguage
        };

        await _userRepository.CreateAsync(user, ct);

        // Auto-login after registration
        var loginResponse = await _authService.AuthenticateAsync(
            new LoginRequest { Email = request.Email, Password = request.Password }, ct);

        _logger.LogInformation("User registered: {Email}", request.Email);
        return CreatedAtAction(nameof(Login), loginResponse);
    }

    /// <summary>
    /// Get current user profile.
    /// </summary>
    [Authorize]
    [HttpGet("me")]
    public async Task<ActionResult<UserDto>> GetProfile(CancellationToken ct)
    {
        var userId = GetUserId();
        var user = await _userRepository.GetByIdAsync(userId, ct);
        if (user == null) return NotFound();

        return Ok(new UserDto
        {
            Id = user.Id,
            Email = user.Email,
            DisplayName = user.DisplayName,
            PreferredLanguage = user.PreferredLanguage,
            ConsentGiven = user.ConsentGiven
        });
    }

    /// <summary>
    /// Update consent status.
    /// </summary>
    [Authorize]
    [HttpPost("consent")]
    public async Task<ActionResult> UpdateConsent([FromBody] ConsentRequest request, CancellationToken ct)
    {
        var userId = GetUserId();
        var user = await _userRepository.GetByIdAsync(userId, ct);
        if (user == null) return NotFound();

        if (request.ConsentGiven)
            user.GiveConsent();
        else
            user.RevokeConsent();

        await _userRepository.UpdateAsync(user, ct);
        return Ok();
    }

    private Guid GetUserId()
    {
        var claim = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
        return Guid.TryParse(claim, out var id) ? id : Guid.Empty;
    }
}

public record RegisterRequest
{
    public string Email { get; init; } = string.Empty;
    public string Password { get; init; } = string.Empty;
    public string DisplayName { get; init; } = string.Empty;
    public string PreferredLanguage { get; init; } = "en";
}

public record ConsentRequest
{
    public bool ConsentGiven { get; init; }
}
