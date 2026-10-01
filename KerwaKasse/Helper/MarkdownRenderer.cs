using System;
using System.Diagnostics;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;
using Markdig;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;

namespace KerwaKasse.Helper
{
    /// <summary>Renders Markdown into a WPF FlowDocument. Covers the subset that occurs in
    /// hand-written texts like the GitHub release notes: headings, paragraphs, lists, bold/italic,
    /// inline code, links and horizontal rules. Unknown elements fall back to their plain text.</summary>
    public static class MarkdownRenderer
    {
        public static FlowDocument ToFlowDocument(string markdown)
        {
            var document = CreateDocument();
            AppendMarkdown(document.Blocks, markdown);
            return document;
        }

        /// <summary>An empty document with the renderer's base styling, for callers that combine
        /// several Markdown texts with blocks of their own.</summary>
        public static FlowDocument CreateDocument() => new FlowDocument
        {
            FontFamily = new FontFamily("Segoe UI"),
            FontSize = 14,
            Foreground = new SolidColorBrush(Color.FromRgb(0x1B, 0x1E, 0x24)),
            PagePadding = new Thickness(0),
            // FlowDocument justifies by default, which looks off for short UI texts.
            TextAlignment = TextAlignment.Left
        };

        public static void AppendMarkdown(BlockCollection target, string markdown)
        {
            foreach (var block in Markdown.Parse(markdown ?? string.Empty))
            {
                var converted = ConvertBlock(block);
                if (converted != null) target.Add(converted);
            }
        }

        private static System.Windows.Documents.Block ConvertBlock(Markdig.Syntax.Block block)
        {
            switch (block)
            {
                case HeadingBlock heading:
                {
                    var paragraph = new Paragraph
                    {
                        FontWeight = FontWeights.SemiBold,
                        FontSize = heading.Level switch { 1 => 18, 2 => 16.5, _ => 15 },
                        Margin = new Thickness(0, 10, 0, 6)
                    };
                    AddInlines(paragraph.Inlines, heading.Inline);
                    return paragraph;
                }
                case ParagraphBlock paragraphBlock:
                {
                    var paragraph = new Paragraph { Margin = new Thickness(0, 0, 0, 8) };
                    AddInlines(paragraph.Inlines, paragraphBlock.Inline);
                    return paragraph;
                }
                case ListBlock listBlock:
                {
                    var list = new List
                    {
                        MarkerStyle = listBlock.IsOrdered ? TextMarkerStyle.Decimal : TextMarkerStyle.Disc,
                        Margin = new Thickness(0, 0, 0, 8),
                        Padding = new Thickness(22, 0, 0, 0)
                    };
                    foreach (var child in listBlock)
                    {
                        if (child is not ListItemBlock itemBlock) continue;
                        var item = new ListItem();
                        foreach (var itemChild in itemBlock)
                        {
                            var converted = ConvertBlock(itemChild);
                            if (converted != null)
                            {
                                // Tighter rhythm inside lists than between top-level paragraphs.
                                if (converted is Paragraph p) p.Margin = new Thickness(0, 0, 0, 4);
                                item.Blocks.Add(converted);
                            }
                        }
                        list.ListItems.Add(item);
                    }
                    return list;
                }
                case QuoteBlock quoteBlock:
                {
                    var section = new Section
                    {
                        BorderBrush = new SolidColorBrush(Color.FromRgb(0xD5, 0xD9, 0xDE)),
                        BorderThickness = new Thickness(3, 0, 0, 0),
                        Padding = new Thickness(10, 0, 0, 0),
                        Margin = new Thickness(0, 0, 0, 8)
                    };
                    foreach (var child in quoteBlock)
                    {
                        var converted = ConvertBlock(child);
                        if (converted != null) section.Blocks.Add(converted);
                    }
                    return section;
                }
                case CodeBlock codeBlock:
                {
                    var paragraph = new Paragraph
                    {
                        FontFamily = new FontFamily("Consolas"),
                        Background = new SolidColorBrush(Color.FromRgb(0xF2, 0xF3, 0xF5)),
                        Padding = new Thickness(8),
                        Margin = new Thickness(0, 0, 0, 8)
                    };
                    paragraph.Inlines.Add(new Run(codeBlock.Lines.ToString()));
                    return paragraph;
                }
                case ThematicBreakBlock:
                    return new Paragraph
                    {
                        BorderBrush = new SolidColorBrush(Color.FromRgb(0xE3, 0xE3, 0xE3)),
                        BorderThickness = new Thickness(0, 0, 0, 1),
                        Margin = new Thickness(0, 4, 0, 12)
                    };
                case LeafBlock leaf: // unknown leaf: at least show its text
                {
                    var paragraph = new Paragraph { Margin = new Thickness(0, 0, 0, 8) };
                    AddInlines(paragraph.Inlines, leaf.Inline);
                    return paragraph;
                }
                default:
                    return null;
            }
        }

        private static void AddInlines(InlineCollection target, ContainerInline source)
        {
            if (source == null) return;
            foreach (var inline in source)
            {
                switch (inline)
                {
                    case LiteralInline literal:
                        target.Add(new Run(literal.Content.ToString()));
                        break;
                    case EmphasisInline emphasis:
                    {
                        var span = new Span();
                        if (emphasis.DelimiterCount >= 2) span.FontWeight = FontWeights.Bold;
                        else span.FontStyle = FontStyles.Italic;
                        AddInlines(span.Inlines, emphasis);
                        target.Add(span);
                        break;
                    }
                    case CodeInline code:
                        target.Add(new Run(code.Content)
                        {
                            FontFamily = new FontFamily("Consolas"),
                            Background = new SolidColorBrush(Color.FromRgb(0xF2, 0xF3, 0xF5))
                        });
                        break;
                    case LinkInline { IsImage: true }:
                        break; // images make no sense in this context, skip them
                    case LinkInline link:
                    {
                        var hyperlink = new Hyperlink();
                        AddInlines(hyperlink.Inlines, link);
                        if (hyperlink.Inlines.Count == 0) hyperlink.Inlines.Add(new Run(link.Url ?? string.Empty));
                        hyperlink.Click += (_, _) => OpenUrl(link.Url);
                        target.Add(hyperlink);
                        break;
                    }
                    case AutolinkInline autolink:
                    {
                        var hyperlink = new Hyperlink(new Run(autolink.Url));
                        hyperlink.Click += (_, _) => OpenUrl(autolink.Url);
                        target.Add(hyperlink);
                        break;
                    }
                    // GitHub renders single line breaks in release notes as real breaks, so both
                    // soft and hard breaks become one here — that matches how the notes look online.
                    case LineBreakInline:
                        target.Add(new LineBreak());
                        break;
                    case ContainerInline container:
                        AddInlines(target, container);
                        break;
                }
            }
        }

        private static void OpenUrl(string url)
        {
            if (string.IsNullOrWhiteSpace(url)) return;
            try
            {
                Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
            }
            catch
            {
                // A link that cannot be opened is not worth an error dialog here.
            }
        }
    }
}
