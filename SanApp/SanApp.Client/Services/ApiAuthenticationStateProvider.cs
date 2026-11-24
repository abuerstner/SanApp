using System.Net;
using System.Security.Claims;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Routing;

namespace SanApp.Client.Services;

public sealed class ApiAuthenticationStateProvider : AuthenticationStateProvider, IDisposable
{
    private readonly UsersApi usersApi;
    private readonly NavigationManager navigation;

    public ApiAuthenticationStateProvider(UsersApi usersApi, NavigationManager navigation)
    {
        this.usersApi = usersApi;
        this.navigation = navigation;
        navigation.LocationChanged += OnLocationChanged;
    }

    public override async Task<AuthenticationState> GetAuthenticationStateAsync()
    {
        UserResponse user;
        try
        {
            user = await usersApi.GetCurrentUserAsync();
        }
        catch (ApiProblemException exception) when (exception.StatusCode == HttpStatusCode.Unauthorized)
        {
            return new AuthenticationState(new ClaimsPrincipal(new ClaimsIdentity()));
        }

        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, user.Id),
            new(ClaimTypes.Name, user.Email),
            new(ClaimTypes.Email, user.Email)
        };
        claims.AddRange(user.Roles.Select(role => new Claim(ClaimTypes.Role, role)));
        var identity = new ClaimsIdentity(claims, "SanApp.Api");
        return new AuthenticationState(new ClaimsPrincipal(identity));
    }

    private void OnLocationChanged(object? sender, LocationChangedEventArgs args) =>
        NotifyAuthenticationStateChanged(GetAuthenticationStateAsync());

    public void Dispose() => navigation.LocationChanged -= OnLocationChanged;
}
