using System.Security.Cryptography;
using System.Text;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using PromptHelper.Models;
using PromptHelper.Services;

namespace PromptHelper.Core.Tests;

[TestClass]
public sealed class LibraryFormatCompatibilityTests
{
    [TestMethod]
    public void Canonical_library_format_is_platform_independent_and_BOM_free()
    {
        Guid categoryId = Guid.Parse("11111111-1111-1111-1111-111111111111");
        Guid promptId = Guid.Parse("22222222-2222-2222-2222-222222222222");

        var document = new LibraryDocument
        {
            SchemaVersion = LibraryDocument.CurrentSchemaVersion,
            PremadePackVersion = 7,
            Categories =
            [
                new CategoryRecord
                {
                    Id = categoryId,
                    ParentId = null,
                    Name = "Cross Platform",
                    SortOrder = 10
                }
            ],
            Prompts =
            [
                new PromptRecord
                {
                    Id = promptId,
                    CategoryId = categoryId,
                    SortOrder = 20,
                    Title = "Hello Linux"
                }
            ]
        };

        CanonicalLibraryPackage package = CanonicalLibraryPackage.Create(document);

        string expected = string.Join(
            "\r\n",
            [
                "{",
                "  \"schemaVersion\": 1,",
                "  \"premadePackVersion\": 7,",
                "  \"categories\": [",
                "    {",
                "      \"id\": \"11111111-1111-1111-1111-111111111111\",",
                "      \"parentId\": null,",
                "      \"name\": \"Cross Platform\",",
                "      \"sortOrder\": 10",
                "    }",
                "  ],",
                "  \"prompts\": [",
                "    {",
                "      \"id\": \"22222222-2222-2222-2222-222222222222\",",
                "      \"categoryId\": \"11111111-1111-1111-1111-111111111111\",",
                "      \"sortOrder\": 20,",
                "      \"title\": \"Hello Linux\"",
                "    }",
                "  ]",
                "}"
            ]);

        CollectionAssert.AreEqual(Encoding.UTF8.GetBytes(expected), package.CanonicalBytes);
        string canonicalText = Encoding.UTF8.GetString(package.CanonicalBytes);
        Assert.IsTrue(canonicalText.Contains("\r\n", StringComparison.Ordinal));
        Assert.IsFalse(canonicalText.Replace("\r\n", string.Empty, StringComparison.Ordinal).Contains('\n'));
        Assert.IsFalse(
            package.CanonicalBytes.AsSpan().StartsWith(new byte[] { 0xEF, 0xBB, 0xBF }),
            "Canonical library JSON must never include a UTF-8 BOM.");
        Assert.AreEqual(
            Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(expected))),
            package.Sha256Hex);
    }

    [TestMethod]
    public void Strict_UTF8_decoder_accepts_BOM_but_encoder_never_emits_one()
    {
        byte[] withBom = [0xEF, 0xBB, 0xBF, .. Encoding.UTF8.GetBytes("Hello")];

        Assert.AreEqual("Hello", StrictUtf8Text.Decode(withBom, "test text"));

        byte[] encoded = StrictUtf8Text.Encode("Hello");
        CollectionAssert.AreEqual(Encoding.UTF8.GetBytes("Hello"), encoded);
    }

    [TestMethod]
    public void Validator_rejects_case_only_duplicate_sibling_categories_on_every_platform()
    {
        var document = new LibraryDocument
        {
            Categories =
            [
                new CategoryRecord
                {
                    Id = Guid.Parse("33333333-3333-3333-3333-333333333333"),
                    Name = "Tools",
                    SortOrder = 10
                },
                new CategoryRecord
                {
                    Id = Guid.Parse("44444444-4444-4444-4444-444444444444"),
                    Name = "tools",
                    SortOrder = 20
                }
            ]
        };

        Assert.Throws<InvalidDataException>(() => LibraryValidator.Validate(document));
    }
}
