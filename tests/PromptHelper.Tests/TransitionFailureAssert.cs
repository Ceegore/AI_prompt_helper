using System;
using System.IO;
using System.Text;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using PromptHelper.Services;

namespace PromptHelper.Tests;

/// <summary>
/// Assertions for "the transition failed and rolled back cleanly".
///
/// Plain <c>Assert.Throws&lt;IOException&gt;</c> cannot express that: ten product exception
/// types derive from <see cref="IOException"/>, and one of them -
/// <see cref="MigrationRollbackException"/> - means the exact opposite, namely that rollback
/// could NOT finish and residue was left behind. A test that accepts it and then asserts the
/// residue is gone fails on the residue line with no indication of why, which is how a
/// genuine rollback failure once looked like an unexplained flake.
/// </summary>
internal static class TransitionFailureAssert
{
    /// <summary>
    /// Asserts that <paramref name="action"/> fails with an ordinary IO error and that
    /// rollback completed, reporting any rollback failures and leftover residue when it did
    /// not. <paramref name="targetRoot"/> is listed on failure when supplied.
    /// </summary>
    public static IOException CleanRollback(Action action, string? targetRoot = null)
    {
        ArgumentNullException.ThrowIfNull(action);

        var caught = Assert.Throws<IOException>(action);

        if (caught is MigrationRollbackException rollback)
        {
            Assert.Fail(Describe(rollback, targetRoot));
        }

        if (caught is TargetRootReservationAcquireException acquire)
        {
            Assert.Fail(Describe(acquire, targetRoot));
        }

        return caught;
    }

    private static string Describe(MigrationRollbackException rollback, string? targetRoot)
    {
        var sb = new StringBuilder();
        sb.AppendLine("Expected a clean rollback, but rollback itself failed.");
        sb.AppendLine($"  original : {rollback.InnerException?.GetType().FullName}: {rollback.InnerException?.Message}");
        sb.AppendLine($"  target   : {rollback.TargetRoot}");

        foreach (MigrationRollbackFailure failure in rollback.Failures)
        {
            sb.AppendLine($"  failure  : op={failure.Operation} path={failure.Path} msg={failure.Message}");
        }

        AppendResidue(sb, targetRoot ?? rollback.TargetRoot);
        return sb.ToString();
    }

    private static string Describe(TargetRootReservationAcquireException acquire, string? targetRoot)
    {
        var sb = new StringBuilder();
        sb.AppendLine("Expected a clean rollback, but the reservation could not be acquired and its own cleanup failed.");
        sb.AppendLine($"  message  : {acquire.Message}");

        if (targetRoot is not null)
        {
            AppendResidue(sb, targetRoot);
        }

        return sb.ToString();
    }

    private static void AppendResidue(StringBuilder sb, string root)
    {
        try
        {
            if (!Directory.Exists(root))
            {
                sb.AppendLine($"  residue  : none ('{root}' does not exist)");
                return;
            }

            string[] entries = Directory.GetFileSystemEntries(root, "*", SearchOption.AllDirectories);
            if (entries.Length == 0)
            {
                sb.AppendLine($"  residue  : '{root}' exists but is empty");
                return;
            }

            foreach (string entry in entries)
            {
                sb.AppendLine($"  residue  : {entry}");
            }
        }
        catch (Exception ex)
        {
            sb.AppendLine($"  residue  : could not be listed ({ex.GetType().Name}: {ex.Message})");
        }
    }
}
