using System.IO;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using PromptHelper.Models;
using PromptHelper.Services;

namespace PromptHelper.Tests;

[TestClass]
public sealed class LibraryValidatorTests
{
    [TestMethod]
    public void Valid_empty_library_passes()
    {
        var doc = new LibraryDocument();
        LibraryValidator.Validate(doc);
    }

    [TestMethod]
    public void Duplicate_category_id_fails()
    {
        var id = Guid.NewGuid();
        var doc = new LibraryDocument
        {
            Categories =
            [
                new CategoryRecord { Id = id, Name = "Cat1", SortOrder = 10 },
                new CategoryRecord { Id = id, Name = "Cat2", SortOrder = 20 }
            ]
        };

        Assert.Throws<InvalidDataException>(() => LibraryValidator.Validate(doc));
    }

    [TestMethod]
    public void Duplicate_prompt_id_fails()
    {
        var id = Guid.NewGuid();
        var doc = new LibraryDocument
        {
            Prompts =
            [
                new PromptRecord { Id = id, SortOrder = 10 },
                new PromptRecord { Id = id, SortOrder = 20 }
            ]
        };

        Assert.Throws<InvalidDataException>(() => LibraryValidator.Validate(doc));
    }

    [TestMethod]
    public void Category_empty_guid_fails()
    {
        var doc = new LibraryDocument
        {
            Categories =
            [
                new CategoryRecord { Id = Guid.Empty, Name = "EmptyGuid", SortOrder = 10 }
            ]
        };

        Assert.Throws<InvalidDataException>(() => LibraryValidator.Validate(doc));
    }

    [TestMethod]
    public void Prompt_empty_guid_fails()
    {
        var doc = new LibraryDocument
        {
            Prompts =
            [
                new PromptRecord { Id = Guid.Empty, SortOrder = 10 }
            ]
        };

        Assert.Throws<InvalidDataException>(() => LibraryValidator.Validate(doc));
    }

    [TestMethod]
    public void Empty_category_name_fails()
    {
        var doc = new LibraryDocument
        {
            Categories =
            [
                new CategoryRecord { Id = Guid.NewGuid(), Name = "", SortOrder = 10 }
            ]
        };

        Assert.Throws<InvalidDataException>(() => LibraryValidator.Validate(doc));
    }

    [TestMethod]
    public void Whitespace_category_name_fails()
    {
        var doc = new LibraryDocument
        {
            Categories =
            [
                new CategoryRecord { Id = Guid.NewGuid(), Name = "   ", SortOrder = 10 }
            ]
        };

        Assert.Throws<InvalidDataException>(() => LibraryValidator.Validate(doc));
    }

    [TestMethod]
    public void Category_not_trimmed_fails()
    {
        var doc = new LibraryDocument
        {
            Categories =
            [
                new CategoryRecord { Id = Guid.NewGuid(), Name = " Trailing ", SortOrder = 10 }
            ]
        };

        Assert.Throws<InvalidDataException>(() => LibraryValidator.Validate(doc));
    }

    [TestMethod]
    public void Category_control_character_fails()
    {
        var doc = new LibraryDocument
        {
            Categories =
            [
                new CategoryRecord { Id = Guid.NewGuid(), Name = "Name\nNewline", SortOrder = 10 }
            ]
        };

        Assert.Throws<InvalidDataException>(() => LibraryValidator.Validate(doc));
    }

    [TestMethod]
    public void Category_over_80_text_elements_fails()
    {
        string longName = new('a', 81);
        var doc = new LibraryDocument
        {
            Categories =
            [
                new CategoryRecord { Id = Guid.NewGuid(), Name = longName, SortOrder = 10 }
            ]
        };

        Assert.Throws<InvalidDataException>(() => LibraryValidator.Validate(doc));
    }

    [TestMethod]
    public void Unknown_parent_fails()
    {
        var doc = new LibraryDocument
        {
            Categories =
            [
                new CategoryRecord { Id = Guid.NewGuid(), ParentId = Guid.NewGuid(), Name = "Cat1", SortOrder = 10 }
            ]
        };

        Assert.Throws<InvalidDataException>(() => LibraryValidator.Validate(doc));
    }

    [TestMethod]
    public void Self_parent_fails()
    {
        var id = Guid.NewGuid();
        var doc = new LibraryDocument
        {
            Categories =
            [
                new CategoryRecord { Id = id, ParentId = id, Name = "Cat1", SortOrder = 10 }
            ]
        };

        Assert.Throws<InvalidDataException>(() => LibraryValidator.Validate(doc));
    }

    [TestMethod]
    public void Two_node_cycle_fails()
    {
        var id1 = Guid.NewGuid();
        var id2 = Guid.NewGuid();
        var doc = new LibraryDocument
        {
            Categories =
            [
                new CategoryRecord { Id = id1, ParentId = id2, Name = "Cat1", SortOrder = 10 },
                new CategoryRecord { Id = id2, ParentId = id1, Name = "Cat2", SortOrder = 20 }
            ]
        };

        Assert.Throws<InvalidDataException>(() => LibraryValidator.Validate(doc));
    }

    [TestMethod]
    public void Three_node_cycle_fails()
    {
        var id1 = Guid.NewGuid();
        var id2 = Guid.NewGuid();
        var id3 = Guid.NewGuid();
        var doc = new LibraryDocument
        {
            Categories =
            [
                new CategoryRecord { Id = id1, ParentId = id2, Name = "Cat1", SortOrder = 10 },
                new CategoryRecord { Id = id2, ParentId = id3, Name = "Cat2", SortOrder = 20 },
                new CategoryRecord { Id = id3, ParentId = id1, Name = "Cat3", SortOrder = 30 }
            ]
        };

        Assert.Throws<InvalidDataException>(() => LibraryValidator.Validate(doc));
    }

    [TestMethod]
    public void Duplicate_sibling_name_fails_case_insensitive()
    {
        var doc = new LibraryDocument
        {
            Categories =
            [
                new CategoryRecord { Id = Guid.NewGuid(), ParentId = null, Name = "Testing", SortOrder = 10 },
                new CategoryRecord { Id = Guid.NewGuid(), ParentId = null, Name = "testing", SortOrder = 20 }
            ]
        };

        Assert.Throws<InvalidDataException>(() => LibraryValidator.Validate(doc));
    }

    [TestMethod]
    public void Same_name_different_parent_passes()
    {
        var parent1 = Guid.NewGuid();
        var parent2 = Guid.NewGuid();
        var doc = new LibraryDocument
        {
            Categories =
            [
                new CategoryRecord { Id = parent1, ParentId = null, Name = "Games", SortOrder = 10 },
                new CategoryRecord { Id = parent2, ParentId = null, Name = "Tools", SortOrder = 20 },
                new CategoryRecord { Id = Guid.NewGuid(), ParentId = parent1, Name = "Testing", SortOrder = 10 },
                new CategoryRecord { Id = Guid.NewGuid(), ParentId = parent2, Name = "Testing", SortOrder = 10 }
            ]
        };

        LibraryValidator.Validate(doc);
    }

    [TestMethod]
    public void Prompt_unknown_category_fails()
    {
        var doc = new LibraryDocument
        {
            Prompts =
            [
                new PromptRecord { Id = Guid.NewGuid(), CategoryId = Guid.NewGuid(), SortOrder = 10 }
            ]
        };

        Assert.Throws<InvalidDataException>(() => LibraryValidator.Validate(doc));
    }
}