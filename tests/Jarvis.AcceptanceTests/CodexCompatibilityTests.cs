using Jarvis.Codex;
using Xunit;

namespace Jarvis.AcceptanceTests;

public sealed class CodexCompatibilityTests
{
    [Theory]
    [InlineData("codex-cli 0.150.0-alpha.8")]
    [InlineData("codex-cli 0.150.0-alpha.12.2")]
    public void Supported_alpha_family_is_accepted(string versionOutput)
    {
        Assert.True(CodexCompatibility.IsSupportedCliVersion(versionOutput));
    }

    [Theory]
    [InlineData("codex-cli 0.149.0")]
    [InlineData("codex-cli 0.151.0-alpha.1")]
    [InlineData("codex-cli 0.150.0-alpha.invalid")]
    public void Different_or_malformed_family_is_rejected(string versionOutput)
    {
        Assert.False(CodexCompatibility.IsSupportedCliVersion(versionOutput));
    }
}
