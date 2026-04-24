using Spark.Models;
using Spark.Services.Prompts;
using Xunit;

namespace Spark.Tests.Unit;

public class UserPromptPersonalizationServiceTests
{
    [Fact]
    public void BuildPrompt_IncludesProfileSection_ForEmailIntent()
    {
        var service = new UserPromptPersonalizationService();
        var settings = new AppSettings
        {
            UserProfile = new UserProfileSettings
            {
                Name = "Akhil",
                Email = "akhil@example.com"
            }
        };

        var prompt = service.BuildPrompt("Write an email using this text: {text}", "hello", settings);

        Assert.Contains("Name: Akhil", prompt);
        Assert.Contains("Email: akhil@example.com", prompt);
        Assert.Contains("Write an email using this text: hello", prompt);
    }

    [Fact]
    public void BuildPrompt_DoesNotIncludeProfileSection_ForGenericRewrite()
    {
        var service = new UserPromptPersonalizationService();
        var settings = new AppSettings
        {
            UserProfile = new UserProfileSettings
            {
                Name = "Akhil",
                Email = "akhil@example.com"
            }
        };

        var prompt = service.BuildPrompt("Rewrite this clearly: {text}", "hello", settings);

        Assert.DoesNotContain("Name: Akhil", prompt);
        Assert.DoesNotContain("Email: akhil@example.com", prompt);
        Assert.Contains("Rewrite this clearly: hello", prompt);
    }

    [Fact]
    public void BuildPrompt_IncludesProfileSection_WhenSelectedTextHasIdentityPlaceholder()
    {
        var service = new UserPromptPersonalizationService();
        var settings = new AppSettings
        {
            UserProfile = new UserProfileSettings
            {
                Name = "Akhil",
                Email = "akhil@example.com"
            }
        };

        var prompt = service.BuildPrompt("Improve this: {text}", "Best,\n[Your Name]", settings);

        Assert.Contains("Name: Akhil", prompt);
        Assert.Contains("Do not add a sign-off", prompt);
    }

    [Fact]
    public void ApplyIdentityPlaceholders_ReplacesNameAndEmailPlaceholders()
    {
        var service = new UserPromptPersonalizationService();
        var settings = new AppSettings
        {
            UserProfile = new UserProfileSettings
            {
                Name = "Akhil",
                Email = "akhil@example.com"
            }
        };

        var output = "Best regards, [Your Name]\nContact: <Your Email>";
        var replaced = service.ApplyIdentityPlaceholders(output, settings);

        Assert.Contains("Akhil", replaced);
        Assert.Contains("akhil@example.com", replaced);
        Assert.DoesNotContain("[Your Name]", replaced);
        Assert.DoesNotContain("<Your Email>", replaced);
    }
}
