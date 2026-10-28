// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// Consumers restore Chuvadi from a local feed with NuGet package source
// mapping on "Chuvadi.*", so every package id must keep that prefix, and
// every package takes the one version build/pack.ps1 passes (-p:Version).

using System;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using FluentAssertions;
using Xunit;

namespace Chuvadi.Pdf.Integration.Tests;

public sealed class PackageIdentityTests
{
    [Fact]
    public void EveryPackableProject_HasAChuvadiPackageId_AndNoVersionOfItsOwn()
    {
        string src = Path.Combine(FindRepoRoot(), "src");
        string[] projects = Directory.GetFiles(src, "*.csproj", SearchOption.AllDirectories);
        projects.Should().NotBeEmpty();

        foreach (string project in projects)
        {
            XDocument xml = XDocument.Load(project);
            string? Property(string name) => xml.Descendants(name).Select(e => e.Value.Trim()).FirstOrDefault();

            if (string.Equals(Property("IsPackable"), "false", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            string id = Property("PackageId") ?? Property("AssemblyName") ?? Path.GetFileNameWithoutExtension(project);
            id.Should().StartWith("Chuvadi.", "{0} must stay under the Chuvadi.* source mapping", project);
            Property("Version").Should().BeNull("{0} must take the single version passed at pack time", project);
            Property("PackageVersion").Should().BeNull("{0} must take the single version passed at pack time", project);
        }
    }

    private static string FindRepoRoot()
    {
        DirectoryInfo? dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Chuvadi.slnx")))
        {
            dir = dir.Parent;
        }

        return dir?.FullName ?? throw new InvalidOperationException("Chuvadi.slnx not found above " + AppContext.BaseDirectory);
    }
}
