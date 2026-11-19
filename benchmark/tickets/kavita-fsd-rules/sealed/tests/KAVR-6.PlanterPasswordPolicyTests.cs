using Kavita.Models.Entities.User;
using Kavita.Server.Extensions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace Kavita.Server.Tests;

// Planter's throwaway test (D406): a new password must be at least 8 characters long.
public class PlanterPasswordPolicyTests
{
    [Fact]
    public async Task SevenCharacterPassword_IsRejected()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["TokenKey"] = new string('k', 64) })
            .Build();
        services.AddIdentityServices(config, Substitute.For<IWebHostEnvironment>());
        await using var sp = services.BuildServiceProvider();

        var options = sp.GetRequiredService<IOptions<IdentityOptions>>();
        var manager = new UserManager<AppUser>(Substitute.For<IUserStore<AppUser>>(), options,
            new PasswordHasher<AppUser>(), [], [new PasswordValidator<AppUser>()], new UpperInvariantLookupNormalizer(),
            new IdentityErrorDescriber(), sp, sp.GetRequiredService<Microsoft.Extensions.Logging.ILogger<UserManager<AppUser>>>());

        var result = await manager.PasswordValidators.First().ValidateAsync(manager, new AppUser { UserName = "reader" }, "abc1234");

        Assert.False(result.Succeeded, "a 7-character password was accepted");
        Assert.Equal(8, options.Value.Password.RequiredLength);
    }
}
