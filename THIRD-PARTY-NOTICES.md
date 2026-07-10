# Third-Party Notices

KerwaKasse uses the open-source components listed below. Each component is the
property of its respective authors and is licensed under its own terms, which
are independent of KerwaKasse's Apache-2.0 license.

## NuGet packages

| Package | License | Project |
| --- | --- | --- |
| Dapper | Apache-2.0 | <https://github.com/DapperLib/Dapper> |
| LiveChartsCore (+ SkiaSharpView, SkiaSharpView.WPF) | MIT | <https://github.com/beto-rodriguez/LiveCharts2> |
| Markdig | BSD-2-Clause | <https://github.com/xoofx/markdig> |
| Microsoft.Data.Sqlite | MIT | <https://github.com/dotnet/efcore> |
| Microsoft.Extensions.Logging.Abstractions | MIT | <https://github.com/dotnet/runtime> |
| ModernWpfUI | MIT | <https://github.com/Kinnara/ModernWpf> |
| QuestPDF | QuestPDF Community License (see below) | <https://github.com/QuestPDF/QuestPDF> |
| Serilog.Extensions.Logging | Apache-2.0 | <https://github.com/serilog/serilog-extensions-logging> |
| Serilog.Sinks.File | Apache-2.0 | <https://github.com/serilog/serilog-sinks-file> |

Notable transitive components distributed with the application:

| Component | License | Project |
| --- | --- | --- |
| Serilog | Apache-2.0 | <https://github.com/serilog/serilog> |
| SkiaSharp | MIT | <https://github.com/mono/SkiaSharp> |
| SQLitePCLraw | Apache-2.0 | <https://github.com/ericsink/SQLitePCL.raw> |
| SQLite | Public Domain | <https://sqlite.org> |
| Lato font (bundled by LiveCharts2) | SIL Open Font License 1.1 | <https://www.latofonts.com> |

## QuestPDF licensing

QuestPDF is **not** covered by this project's Apache-2.0 license. KerwaKasse
uses QuestPDF under the QuestPDF Community License.

At the time of writing, the QuestPDF Community License is available free of
charge for certain eligible users and projects, including individuals and
small businesses below the stated revenue threshold, qualifying charitable or
academic organisations, eligible open-source projects distributed under an
OSI-approved license, and certain transitive-dependency users.

The QuestPDF Community License is a source-available commercial license. It
is not an OSI-approved open-source license, and QuestPDF is not licensed
under the MIT License.

End users who only use KerwaKasse as an application typically do not interact
with QuestPDF APIs directly. Forks, modified versions, commercial users, and
anyone working directly with QuestPDF APIs should verify their own
eligibility under the applicable QuestPDF license terms:
<https://www.questpdf.com/license/>.
