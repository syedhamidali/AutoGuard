using System.Diagnostics;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using AutoPortal.Guardian.Windows.Startup;
using Xunit;

namespace AutoPortal.Guardian.Tests;

/// <summary>
/// Executable documentation for the build/release scripts and CI workflows: each test states what a script is
/// MEANT to do, so a failing CI run tells you which promise was broken.
/// </summary>
static class Repo
{
    public static string Root
    {
        get
        {
            var d = new DirectoryInfo(AppContext.BaseDirectory);
            while (d != null && !File.Exists(Path.Combine(d.FullName, "AutoPortal.SystemGuardian.sln"))) d = d.Parent;
            return d?.FullName ?? throw new InvalidOperationException("Repository root not found.");
        }
    }
    public static string Read(string rel) => File.ReadAllText(Path.Combine(Root, rel));

    public static (int code, string output) Ps(string args)
    {
        var psi = new ProcessStartInfo("powershell.exe", "-NoProfile -ExecutionPolicy Bypass " + args)
        { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true };
        // When the test host is started from PowerShell 7 (as on CI), its PSModulePath points at modules Windows PowerShell 5.1
        // cannot load, so built-in cmdlets like Get-FileHash disappear. Let the child use its own default module path.
        psi.Environment.Remove("PSModulePath");
        using var p = Process.Start(psi)!;
        var o = p.StandardOutput.ReadToEnd() + p.StandardError.ReadToEnd();
        p.WaitForExit(); return (p.ExitCode, o);
    }
}

public class BuildScriptTests
{
    [Theory]
    [InlineData("scripts/build.ps1")]
    [InlineData("scripts/verify-artifacts.ps1")]
    public void ScriptsHaveNoSyntaxErrors(string rel)
    {
        var path = Path.Combine(Repo.Root, rel);
        var (code, output) = Repo.Ps($"-Command \"$e=$null; [void][System.Management.Automation.Language.Parser]::ParseFile('{path}',[ref]$null,[ref]$e); if($e){{ $e | % Message; exit 1 }}\"");
        Assert.True(code == 0, $"{rel} has syntax errors:\n{output}");
    }

    [Fact]
    public void BuildScriptRunsTestsBeforePackagingAndStopsIfTheyFail()
    {
        var s = Repo.Read("scripts/build.ps1");
        Assert.True(s.IndexOf("dotnet test", StringComparison.Ordinal) < s.IndexOf("dotnet publish", StringComparison.Ordinal), "tests must run before publishing");
        Assert.Matches(@"if \(\$LASTEXITCODE\) \{ throw ""Tests failed"" \}", s);
    }

    [Fact]
    public void BuildScriptPublishesBothModulesAsSelfContainedSingleFile()
    {
        var s = Repo.Read("scripts/build.ps1");
        Assert.Contains("AutoPortal.Guardian.App.csproj", s);
        Assert.Contains("AutoPortal.Connectivity.App.csproj", s);
        Assert.Contains("--self-contained true", s);       // end users need no .NET install
        Assert.Contains("PublishSingleFile=true", s);
        Assert.Contains("SHA256SUMS.txt", s);
    }

    [Fact]
    public void EveryProjectAndInstallerTheBuildScriptReferencesExists()
    {
        var s = Repo.Read("scripts/build.ps1");
        foreach (Match m in Regex.Matches(s, @"(src/[\w/\.]+\.csproj)")) Assert.True(File.Exists(Path.Combine(Repo.Root, m.Value)), $"missing project {m.Value}");
        foreach (Match m in Regex.Matches(s, @"Iss = ""([\w\.]+)""")) Assert.True(File.Exists(Path.Combine(Repo.Root, "installer", m.Groups[1].Value)), $"missing installer script {m.Groups[1].Value}");
    }

    [Fact]
    public void BuildScriptEndsByVerifyingItsOwnOutput() =>
        Assert.Contains("verify-artifacts.ps1", Repo.Read("scripts/build.ps1"));
}

public class VerifyArtifactsScriptTests : IDisposable
{
    readonly string _dir = Path.Combine(Path.GetTempPath(), "apsg-art-" + Guid.NewGuid());
    const string V = "9.9.9";
    public VerifyArtifactsScriptTests() => Directory.CreateDirectory(_dir);
    public void Dispose() { try { Directory.Delete(_dir, true); } catch { } }

    void MakeZip(string name, string exe)
    {
        var tmp = Path.Combine(_dir, "stage-" + Guid.NewGuid()); Directory.CreateDirectory(tmp);
        File.WriteAllText(Path.Combine(tmp, exe), "x"); File.WriteAllText(Path.Combine(tmp, "LICENSE"), "MIT"); File.WriteAllText(Path.Combine(tmp, "THIRD-PARTY-NOTICES.md"), "n");
        ZipFile.CreateFromDirectory(tmp, Path.Combine(_dir, name)); Directory.Delete(tmp, true);
    }

    void WriteSums()
    {
        var lines = Directory.GetFiles(_dir).Where(f => !f.EndsWith("SHA256SUMS.txt")).Select(f => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(f))).ToLowerInvariant() + "  " + Path.GetFileName(f));
        File.WriteAllLines(Path.Combine(_dir, "SHA256SUMS.txt"), lines);
    }

    void GoodSet()
    {
        MakeZip($"AutoPortal-System-Guardian-Portable-{V}.zip", "AutoPortalSystemGuardian.exe");
        MakeZip($"AutoPortal-Connectivity-Manager-Portable-{V}.zip", "AutoPortalConnectivityManager.exe");
        WriteSums();
    }

    (int code, string output) Run(bool installers = false) =>
        Repo.Ps($"-File \"{Path.Combine(Repo.Root, "scripts", "verify-artifacts.ps1")}\" -ArtifactsDir \"{_dir}\" -Version {V}" + (installers ? " -RequireInstallers" : ""));

    [Fact] public void PassesForAGoodSet() { GoodSet(); var (c, o) = Run(); Assert.True(c == 0, o); }

    [Fact]
    public void FailsWhenAFileWasTamperedWithAfterChecksumming()
    {
        GoodSet(); File.AppendAllText(Path.Combine(_dir, $"AutoPortal-System-Guardian-Portable-{V}.zip"), "tamper");
        var (c, o) = Run(); Assert.NotEqual(0, c); Assert.Contains("Checksum mismatch", o);
    }

    [Fact]
    public void FailsWhenAModuleIsMissing()
    {
        GoodSet(); File.Delete(Path.Combine(_dir, $"AutoPortal-Connectivity-Manager-Portable-{V}.zip")); WriteSums();
        var (c, o) = Run(); Assert.NotEqual(0, c); Assert.Contains("Missing artifact", o);
    }

    [Fact]
    public void FailsWhenInstallersAreRequiredButAbsent()
    {
        GoodSet(); var (c, o) = Run(installers: true); Assert.NotEqual(0, c); Assert.Contains("Setup", o);
    }

    [Fact]
    public void FailsWhenTheZipLacksTheExecutable()
    {
        GoodSet(); File.Delete(Path.Combine(_dir, $"AutoPortal-System-Guardian-Portable-{V}.zip"));
        MakeZip($"AutoPortal-System-Guardian-Portable-{V}.zip", "wrong.exe"); WriteSums();
        var (c, o) = Run(); Assert.NotEqual(0, c); Assert.Contains("does not contain AutoPortalSystemGuardian.exe", o);
    }

    [Fact]
    public void FailsWhenChecksumFileIsMissing()
    {
        GoodSet(); File.Delete(Path.Combine(_dir, "SHA256SUMS.txt"));
        var (c, o) = Run(); Assert.NotEqual(0, c); Assert.Contains("SHA256SUMS", o);
    }
}

public class InstallerScriptTests
{
    [Theory]
    [InlineData("installer/guardian.iss", "AutoPortalSystemGuardian.exe", "AutoPortal System Guardian")]
    [InlineData("installer/connectivity.iss", "AutoPortalConnectivityManager.exe", "AutoPortal Connectivity Manager")]
    public void InstallerLaunchesTheRightExeAndRemovesOnlyItsOwnStartupTask(string rel, string exe, string taskName)
    {
        var s = Repo.Read(rel);
        Assert.Contains(exe, s);
        Assert.Matches(@"AppId=\{\{[0-9A-F]{8}-[0-9A-F]{4}-[0-9A-F]{4}-[0-9A-F]{4}-[0-9A-F]{12}\}", s); // must be a real GUID
        Assert.Contains($"/Delete /TN \"\"{taskName}\"\" /F", s);   // uninstall deletes exactly this app's task
        Assert.Contains("LICENSE", s);
        Assert.DoesNotContain("/Delete /TN \"\"*", s);
    }

    [Fact]
    public void TheTwoModulesHaveDifferentAppIdsSoTheyCanBeInstalledTogether()
    {
        string Id(string f) => Regex.Match(Repo.Read(f), @"AppId=\{\{([0-9A-Fa-f-]+)\}").Groups[1].Value;
        Assert.NotEqual(Id("installer/guardian.iss"), Id("installer/connectivity.iss"));
    }

    [Fact]
    public void GuardianInstallerTaskNameMatchesTheTaskTheAppCreates() =>
        Assert.Contains($"\"\"{StartupTask.TaskName}\"\"", Repo.Read("installer/guardian.iss"));

    [Fact]
    public void ConnectivityInstallerTaskNameMatchesTheTaskTheAppCreates()
    {
        var program = Repo.Read("src/connectivity/App/Program.cs");
        var name = Regex.Match(program, @"const string TaskName = ""([^""]+)""").Groups[1].Value;
        Assert.False(string.IsNullOrEmpty(name));
        Assert.Contains($"\"\"{name}\"\"", Repo.Read("installer/connectivity.iss"));
    }

    [Fact]
    public void InstallerOutputNamesMatchWhatTheVerifierAndReleaseExpect()
    {
        Assert.Contains("AutoPortal-System-Guardian-Setup-{#AppVersion}", Repo.Read("installer/guardian.iss"));
        Assert.Contains("AutoPortal-Connectivity-Manager-Setup-{#AppVersion}", Repo.Read("installer/connectivity.iss"));
        var v = Repo.Read("scripts/verify-artifacts.ps1");
        Assert.Contains("AutoPortal-System-Guardian-Setup-$Version.exe", v);
        Assert.Contains("AutoPortal-Connectivity-Manager-Setup-$Version.exe", v);
    }
}

public class WorkflowTests
{
    [Fact]
    public void CiBuildsAndRunsTestsOnWindowsForEveryPushAndPullRequest()
    {
        var y = Repo.Read(".github/workflows/ci.yml");
        Assert.Contains("windows-latest", y); Assert.Contains("dotnet test", y); Assert.Contains("pull_request", y);
        Assert.Contains("8.0.x", y);
    }

    [Fact]
    public void CiAlsoVerifiesTheBuildScriptEndToEnd()
    {
        var y = Repo.Read(".github/workflows/ci.yml");
        Assert.Contains("scripts/build.ps1", y);            // packaging must not rot between releases
    }

    [Fact]
    public void ReleaseOnlyRunsOnVersionTagsAndPublishesEveryArtifact()
    {
        var y = Repo.Read(".github/workflows/release.yml");
        Assert.Contains("tags: ['v*']", y);
        Assert.Contains("contents: write", y);
        Assert.Contains("scripts/build.ps1", y);
        Assert.Contains("-RequireInstallers", y);           // a release with no installer must fail
        foreach (var f in new[] { "*.exe", "*.zip", "SHA256SUMS.txt" }) Assert.Contains("artifacts/" + f, y);
    }

    [Fact]
    public void WorkflowFilesAreValidYamlWithTheExpectedTopLevelKeys()
    {
        foreach (var f in new[] { ".github/workflows/ci.yml", ".github/workflows/release.yml" })
        {
            var y = Repo.Read(f);
            Assert.DoesNotContain("\t", y);                 // tabs break YAML
            Assert.Matches(@"(?m)^name: .+", y); Assert.Matches(@"(?m)^on:", y); Assert.Matches(@"(?m)^jobs:", y);
        }
    }
}

public class RepoHygieneTests
{
    [Fact]
    public void RequiredProjectFilesExist()
    {
        foreach (var f in new[] { "README.md", "LICENSE", "CONTRIBUTING.md", "CODE_OF_CONDUCT.md", "SECURITY.md", "CHANGELOG.md", "THIRD-PARTY-NOTICES.md" })
            Assert.True(File.Exists(Path.Combine(Repo.Root, f)), "missing " + f);
    }

    [Fact]
    public void PersonalDataAndBuildOutputsAreNeverCommitted()
    {
        var g = Repo.Read(".gitignore");
        foreach (var p in new[] { "bin/", "obj/", "artifacts/", "backup/", "*.db" }) Assert.Contains(p, g);
    }

    [Fact]
    public void ThirdPartyNoticesCoverTheSensorLibrary() => Assert.Contains("LibreHardwareMonitor", Repo.Read("THIRD-PARTY-NOTICES.md"));

    [Fact]
    public void ChangelogMentionsTheCurrentVersion()
    {
        var v = Regex.Match(Repo.Read("Directory.Build.props"), @"<Version>([^<]+)</Version>").Groups[1].Value;
        Assert.Contains(v, Repo.Read("CHANGELOG.md"));
    }
}
