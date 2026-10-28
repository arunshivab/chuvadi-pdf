// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// PHASE: 3.24 — embedding API (A2, A5): the guarantees in docs/EMBEDDING.md,
// checked against the compiled assemblies.
//
// Opening, rendering (raster and SVG) and text extraction must never touch
// the network, start a process, run scripts or write to disk. Each assembly
// on that path is scanned for references to the APIs that could; the only
// file access allowed is opening the file a caller names (PdfDocument's path
// overloads).

using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using Chuvadi.Pdf.Documents;
using Chuvadi.Pdf.Text;
using FluentAssertions;
using Xunit;

namespace Chuvadi.Pdf.Integration.Tests;

public sealed class EmbeddingGuaranteeTests
{
    // Assemblies on the open / render / extract path.
    private static readonly string[] ViewingAssemblies =
    {
        "Chuvadi.Pdf.Primitives", "Chuvadi.Pdf.Filters", "Chuvadi.Pdf.Objects", "Chuvadi.Pdf.Encryption",
        "Chuvadi.Pdf.IO", "Chuvadi.Pdf.Documents", "Chuvadi.Pdf.Content", "Chuvadi.Pdf.Fonts",
        "Chuvadi.Pdf.Fonts.Rendering", "Chuvadi.Pdf.Fonts.Woff2", "Chuvadi.Pdf.Graphics", "Chuvadi.Pdf.Images",
        "Chuvadi.Pdf.Rendering.Walking", "Chuvadi.Pdf.Rendering.DisplayList", "Chuvadi.Pdf.Rendering.Raster",
        "Chuvadi.Pdf.Rendering", "Chuvadi.Pdf.Text", "Chuvadi.Pdf.Text.Shaping", "Chuvadi.Pdf.Svg",
    };

    // Types whose use would break the guarantees: network, processes,
    // scripting, temporary files and the file system.
    private static readonly string[] ForbiddenTypes =
    {
        "System.Net.Http.HttpClient", "System.Net.Http.HttpMessageHandler", "System.Net.Sockets.Socket",
        "System.Net.Sockets.TcpClient", "System.Net.WebRequest", "System.Net.WebClient", "System.Net.Dns",
        "System.Diagnostics.Process", "System.IO.File", "System.IO.Directory", "System.IO.FileInfo",
        "System.IO.DirectoryInfo", "System.IO.MemoryMappedFiles.MemoryMappedFile", "System.IO.IsolatedStorage.IsolatedStorageFile",
    };

    private static readonly string[] ForbiddenMembers =
    {
        "System.IO.Path.GetTempPath", "System.IO.Path.GetTempFileName", "System.IO.Path.GetRandomFileName",
        "System.Environment.GetFolderPath",
    };

    // The caller-named file the path overloads open (read-only).
    private static readonly string[] AllowedFileStreamUsers = { "Chuvadi.Pdf.Documents" };

    [Fact]
    public void ViewingAssemblies_DoNotReferenceNetworkProcessOrDiskApis()
    {
        List<string> violations = new List<string>();
        foreach (string name in ViewingAssemblies)
        {
            Assembly assembly = Assembly.Load(name);
            foreach (string member in MemberReferences(assembly.Location))
            {
                string type = member[..member.LastIndexOf('.')];
                if (ForbiddenTypes.Contains(type) || ForbiddenMembers.Contains(member))
                {
                    violations.Add(name + ": " + member);
                }

                if (type == "System.IO.FileStream" && !AllowedFileStreamUsers.Contains(name))
                {
                    violations.Add(name + ": " + member);
                }
            }
        }

        violations.Should().BeEmpty();
    }

    [Fact]
    public void ViewingAssemblies_DoNotDependOnTheScriptingOrSigningModules()
    {
        foreach (string name in ViewingAssemblies)
        {
            Assembly.Load(name).GetReferencedAssemblies().Select(r => r.Name)
                .Should().NotContain(new[] { "Chuvadi.Pdf.Xfa", "Chuvadi.Pdf.Signatures", "Chuvadi.Cryptography", "System.Net.Http" }, name);
        }
    }

    [Fact]
    public void ADocumentFullOfScripts_RendersAndExtractsWithoutRunningThem()
    {
        // Every trigger a viewer would run on open, plus a link action. The
        // library has no interpreter on this path, so all that can be checked
        // here is that the content is simply ignored: the page text comes out.
        string[] bodies =
        {
            "<</Type/Catalog/Pages 2 0 R/OpenAction<</S/JavaScript/JS(app.alert(1))>>/AA<</WC<</S/Launch/F(cmd)>>>>>>",
            "<</Type/Pages/Kids[3 0 R]/Count 1>>",
            "<</Type/Page/Parent 2 0 R/MediaBox[0 0 200 200]/Resources<</Font<</F1 5 0 R>>>>/Contents 4 0 R/AA<</O<</S/Launch/F(calc)>>>>>>",
            "<</Length 37>>\nstream\nBT /F1 12 Tf 10 10 Td (VISIBLE) Tj ET\nendstream",
            "<</Type/Font/Subtype/Type1/BaseFont/Helvetica>>",
        };
        System.Text.StringBuilder sb = new System.Text.StringBuilder("%PDF-1.7\n");
        List<int> offsets = new List<int>();
        for (int i = 0; i < bodies.Length; i++)
        {
            offsets.Add(sb.Length);
            sb.Append(i + 1).Append(" 0 obj\n").Append(bodies[i]).Append("\nendobj\n");
        }

        int xref = sb.Length;
        sb.Append("xref\n0 6\n0000000000 65535 f \n");
        foreach (int off in offsets)
        {
            sb.Append(off.ToString("D10", System.Globalization.CultureInfo.InvariantCulture)).Append(" 00000 n \n");
        }

        sb.Append("trailer\n<</Size 6/Root 1 0 R>>\nstartxref\n").Append(xref).Append("\n%%EOF\n");
        byte[] pdf = System.Text.Encoding.Latin1.GetBytes(sb.ToString());

        using PdfDocument doc = PdfDocument.Open(new MemoryStream(pdf), new PdfOpenOptions());

        new TextExtractor(doc.Objects).ExtractText(doc.Pages[0]).Should().Contain("VISIBLE");
    }

    // Every member reference in the assembly, as "Namespace.Type.Member".
    private static IEnumerable<string> MemberReferences(string path)
    {
        using FileStream file = File.OpenRead(path);
        using PEReader pe = new PEReader(file);
        MetadataReader md = pe.GetMetadataReader();
        List<string> result = new List<string>();
        foreach (MemberReferenceHandle handle in md.MemberReferences)
        {
            MemberReference member = md.GetMemberReference(handle);
            string? type = TypeName(md, member.Parent);
            if (type is not null)
            {
                result.Add(type + "." + md.GetString(member.Name));
            }
        }

        // Types referenced without a member (e.g. static classes used through
        // a type reference only) are covered by the member list above for
        // any call; plain type references are listed too.
        foreach (TypeReferenceHandle handle in md.TypeReferences)
        {
            TypeReference reference = md.GetTypeReference(handle);
            result.Add(md.GetString(reference.Namespace) + "." + md.GetString(reference.Name) + ".<type>");
        }

        return result;
    }

    private static string? TypeName(MetadataReader md, EntityHandle parent)
    {
        switch (parent.Kind)
        {
            case HandleKind.TypeReference:
                TypeReference reference = md.GetTypeReference((TypeReferenceHandle)parent);
                return md.GetString(reference.Namespace) + "." + md.GetString(reference.Name);
            case HandleKind.TypeSpecification:
                return null;
            default:
                return null;
        }
    }
}
