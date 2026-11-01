using System.Text;
using Ganjineh.Application.Import;
using Xunit;

namespace Ganjineh.Application.Tests;

/// <summary>
/// Unit coverage for the browser CSV import pipeline (Phase 9 §14): schema matching, header
/// mapping, RFC 4180 edge cases, limits, URL validation and duplicate-key normalization.
/// Vault-bound behavior (duplicate matching, writes) lives in the integration suite.
/// </summary>
public sealed class BrowserImportTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("ganjineh-import-tests").FullName;

    private string WriteCsv(string content, bool bom = false)
    {
        var path = Path.Combine(_dir, Path.GetRandomFileName() + ".csv");
        var bytes = Encoding.UTF8.GetBytes(content);
        if (bom)
        {
            bytes = [0xEF, 0xBB, 0xBF, .. bytes];
        }

        File.WriteAllBytes(path, bytes);
        return path;
    }

    // ── schema detection ──

    [Fact]
    public void Chrome_Standard_Headers_Are_Detected()
    {
        Assert.Equal(ImportBrowser.Chrome, BrowserCsvSchema.Detect(["name", "url", "username", "password", "note"]));
    }

    [Fact]
    public void Firefox_Standard_Headers_Are_Detected()
    {
        Assert.Equal(ImportBrowser.Firefox, BrowserCsvSchema.Detect([
            "url", "username", "password", "httpRealm", "formActionOrigin",
            "guid", "timeCreated", "timeLastUsed", "timePasswordChanged"]));
    }

    [Fact]
    public void Unknown_Headers_Are_Not_Detected()
    {
        Assert.Null(BrowserCsvSchema.Detect(["a", "b", "c"]));
        Assert.Null(BrowserCsvSchema.Detect(["url", "username"])); // password missing
    }

    [Fact]
    public void Chrome_Matches_Rejects_Firefox_Markers_Only_File()
    {
        var firefox = new[] { "url", "username", "password", "httprealm" };
        Assert.True(BrowserCsvSchema.Matches(ImportBrowser.Firefox, firefox));
        // A Firefox file without any marker column is indistinguishable; a name-less file still
        // matches Chrome (documented leniency), but a file WITH markers is not Chrome.
        Assert.False(BrowserCsvSchema.Matches(ImportBrowser.Chrome, firefox));
    }

    // ── mapping ──

    [Fact]
    public void Chrome_Standard_File_Maps_All_Fields()
    {
        var csv = "name,url,username,password,note\n" +
                  "ملی بانک,https://bank.ir/login,ali@x.ir,«رمز»,یادداشت فارسی\n";
        var path = WriteCsv(csv);
        var parsed = new BrowserImportService().ParseFile(ImportBrowser.Chrome, path);

        var row = Assert.Single(parsed.Rows);
        Assert.Equal(ImportRecordStatus.Ready, row.Status);
        Assert.Equal("ملی بانک", row.Title);
        Assert.Equal("ali@x.ir", row.Username);
        Assert.Equal("bank.ir/login", row.OriginDisplay);
        Assert.Empty(parsed.UnsupportedColumns);
        var secret = parsed.Secrets[row.RowNumber];
        Assert.Equal("«رمز»", secret.Password);
        Assert.Equal("یادداشت فارسی", secret.Note);
        Assert.Equal("https://bank.ir/login", secret.Url);
    }

    [Fact]
    public void Reordered_Headers_Map_By_Name()
    {
        var csv = "password,url,note,username,name\n" +
                  "p2,https://a.com,,user2,SiteA\n";
        var path = WriteCsv(csv);
        var parsed = new BrowserImportService().ParseFile(ImportBrowser.Chrome, path);
        var row = Assert.Single(parsed.Rows);
        Assert.Equal("SiteA", row.Title);
        Assert.Equal("user2", row.Username);
        Assert.Equal("p2", parsed.Secrets[row.RowNumber].Password);
    }

    [Fact]
    public void Missing_Optional_Columns_Are_Safe_And_Reported()
    {
        // Chrome with no note column; an extra unknown column must be reported, never dropped silently.
        var csv = "name,url,username,password,favorite\n" +
                  "site,https://s.com,u,p,1\n";
        var path = WriteCsv(csv);
        var parsed = new BrowserImportService().ParseFile(ImportBrowser.Chrome, path);
        var row = Assert.Single(parsed.Rows);
        Assert.Equal(ImportRecordStatus.Ready, row.Status);
        Assert.Equal(["favorite"], parsed.UnsupportedColumns);
        Assert.Null(parsed.Secrets[row.RowNumber].Note);
    }

    [Fact]
    public void Firefox_Standard_File_Maps_Guids_And_Timestamps()
    {
        var csv = "url,username,password,httpRealm,formActionOrigin,guid,timeCreated,timeLastUsed,timePasswordChanged\n" +
                  "https://mail.ir/,«کاربر»,رمز-۱۲۳,,, {AB12-CD34},1747000000000000,1747000001000000,1747000002000000\n";
        var path = WriteCsv(csv);
        var parsed = new BrowserImportService().ParseFile(ImportBrowser.Firefox, path);
        var row = Assert.Single(parsed.Rows);
        Assert.Equal(ImportRecordStatus.Ready, row.Status);
        var secret = parsed.Secrets[row.RowNumber];
        Assert.Equal("{AB12-CD34}", secret.SourceRecordId);
        // PRTime microseconds → Unix seconds (1747000002).
        Assert.Equal(1747000002L, secret.SourceTimePasswordChanged);
        // No title column: the host is derived, nothing invented.
        Assert.Equal("mail.ir", row.Title);
    }

    [Fact]
    public void Firefox_Missing_Fields_Are_Not_Invented()
    {
        var csv = "url,username,password,httpRealm,formActionOrigin,guid,timeCreated,timeLastUsed,timePasswordChanged\n" +
                  "https://x.com,user1,,,,,,,\n";
        var path = WriteCsv(csv);
        var parsed = new BrowserImportService().ParseFile(ImportBrowser.Firefox, path);
        var row = Assert.Single(parsed.Rows);
        Assert.Equal(ImportRecordStatus.Ready, row.Status);
        var secret = parsed.Secrets[row.RowNumber];
        Assert.Null(secret.Note);
        Assert.Null(secret.SourceTimePasswordChanged);
        Assert.Equal("x.com", row.Title);
    }

    // ── RFC 4180 mechanics (through Sylvan) ──

    [Fact]
    public void Quoted_Commas_Escaped_Quotes_And_Multiline_Fields_Parse()
    {
        var csv = "name,url,username,password,note\n" +
                  "\"شرکت پارس، Ltd\",https://pars.ir,u,\"p\"\"q\",\"خط اول\nخط دوم\"\n";
        var path = WriteCsv(csv);
        var parsed = new BrowserImportService().ParseFile(ImportBrowser.Chrome, path);
        var row = Assert.Single(parsed.Rows);
        Assert.Equal("شرکت پارس، Ltd", row.Title);
        var secret = parsed.Secrets[row.RowNumber];
        Assert.Equal("p\"q", secret.Password);
        Assert.Contains("خط دوم", secret.Note);
    }

    [Fact]
    public void Utf8_Bom_Is_Handled()
    {
        var csv = "name,url,username,password,note\nنام,https://b.ir,u,p,\n";
        var path = WriteCsv(csv, bom: true);
        var parsed = new BrowserImportService().ParseFile(ImportBrowser.Chrome, path);
        var row = Assert.Single(parsed.Rows);
        Assert.Equal("نام", row.Title);
    }

    [Fact]
    public void CrLf_And_Lf_Line_Endings_Both_Parse()
    {
        var path = Path.Combine(_dir, "mixed.csv");
        File.WriteAllBytes(path, Encoding.UTF8.GetBytes("name,url,username,password,note\r\na,https://a.com,u1,p1,\nb,https://b.com,u2,p2,\r\n"));
        var parsed = new BrowserImportService().ParseFile(ImportBrowser.Chrome, path);
        Assert.Equal(2, parsed.Rows.Count);
    }

    [Fact]
    public void Duplicate_Headers_Resolve_To_First_Occurrence()
    {
        var csv = "name,url,username,password,url\n" +
                  "site,https://first.com,u,p,https://second.com\n";
        var path = WriteCsv(csv);
        var parsed = new BrowserImportService().ParseFile(ImportBrowser.Chrome, path);
        var row = Assert.Single(parsed.Rows);
        Assert.Equal("https://first.com", parsed.Secrets[row.RowNumber].Url);
        // The second url occurrence is reported as unsupported rather than silently dropped.
        Assert.Contains("url", parsed.UnsupportedColumns);
    }

    [Fact]
    public void Unterminated_Quote_Is_Rejected_As_Malformed()
    {
        var csv = "name,url,username,password,note\nsite,https://a.com,u,\"unterminated\n";
        var path = WriteCsv(csv);
        Assert.ThrowsAny<ImportFormatException>(() =>
            new BrowserImportService().ParseFile(ImportBrowser.Chrome, path));
    }

    // ── limits ──

    [Fact]
    public void Oversized_File_Is_Rejected()
    {
        var path = WriteCsv("name,url,username,password,note\nbig,https://b.com,u,p," + new string('x', 64) + "\n");
        var limits = new ImportFileLimits { MaxFileBytes = 32 };
        Assert.Throws<ImportFormatException>(() =>
            new BrowserImportService().ParseFile(ImportBrowser.Chrome, path, limits));
    }

    [Fact]
    public void Missing_File_Is_Rejected()
    {
        Assert.Throws<ImportFormatException>(() =>
            new BrowserImportService().ParseFile(ImportBrowser.Chrome, Path.Combine(_dir, "nope.csv")));
    }

    [Fact]
    public void Invalid_Utf8_Is_Rejected()
    {
        var path = Path.Combine(_dir, "bad.csv");
        File.WriteAllBytes(path, [0x6E, 0x61, 0x6D, 0x65, 0x0D, 0x0A, 0xFF, 0xFE, 0x0D, 0x0A]); // invalid UTF-8 payload row
        Assert.Throws<ImportFormatException>(() =>
            new BrowserImportService().ParseFile(ImportBrowser.Chrome, path));
    }

    // ── row validation ──

    [Fact]
    public void Empty_Rows_Are_Counted_And_Skipped()
    {
        var csv = "name,url,username,password,note\n" +
                  ",,,,,\n" +
                  "site,https://s.com,u,p,\n";
        var path = WriteCsv(csv);
        var parsed = new BrowserImportService().ParseFile(ImportBrowser.Chrome, path);
        Assert.Single(parsed.Rows);
        Assert.Equal(1, parsed.EmptyRowCount);
    }

    [Fact]
    public void Empty_Password_Is_Importable()
    {
        var csv = "name,url,username,password,note\nsite,https://s.com,user,\n";
        var path = WriteCsv(csv);
        var parsed = new BrowserImportService().ParseFile(ImportBrowser.Chrome, path);
        var row = Assert.Single(parsed.Rows);
        Assert.Equal(ImportRecordStatus.Ready, row.Status);
        Assert.Null(parsed.Secrets[row.RowNumber].Password);
    }

    [Fact]
    public void Invalid_Url_Schemes_Are_Rejected_As_Invalid_Rows()
    {
        var csv = "name,url,username,password,note\n" +
                  "bad,javascript:alert(1),u,p,\n" +
                  "good,https://good.com,u,p,\n";
        var path = WriteCsv(csv);
        var parsed = new BrowserImportService().ParseFile(ImportBrowser.Chrome, path);
        Assert.Equal(2, parsed.Rows.Count);
        var bad = parsed.Rows.Single(r => r.Title == "bad");
        Assert.Equal(ImportRecordStatus.Invalid, bad.Status);
        Assert.Equal("Import.RowInvalidUrl", bad.InvalidReasonKey);
        Assert.Equal(ImportRecordStatus.Ready, parsed.Rows.Single(r => r.Title == "good").Status);
    }

    [Fact]
    public void Bare_Host_Urls_Are_Accepted()
    {
        var csv = "name,url,username,password,note\nsite,example.com/path,u,p,\n";
        var path = WriteCsv(csv);
        var parsed = new BrowserImportService().ParseFile(ImportBrowser.Chrome, path);
        Assert.Equal(ImportRecordStatus.Ready, Assert.Single(parsed.Rows).Status);
    }

    [Fact]
    public void Oversized_Title_Is_Rejected_As_Invalid_Row()
    {
        var csv = "name,url,username,password,note\n" +
                  new string('ع', 300) + ",https://s.com,u,p,\n";
        var path = WriteCsv(csv);
        var parsed = new BrowserImportService().ParseFile(ImportBrowser.Chrome, path);
        var row = Assert.Single(parsed.Rows);
        Assert.Equal(ImportRecordStatus.Invalid, row.Status);
        Assert.Equal("Import.RowInvalidTitle", row.InvalidReasonKey);
    }

    [Fact]
    public void Duplicate_Rows_Within_File_Are_Marked()
    {
        var csv = "name,url,username,password,note\n" +
                  "site,https://s.com,a@x.com,p1,\n" +
                  "site2,https://s.com,a@x.com,p2,\n" +
                  "other,https://s.com,b@x.com,p3,\n";
        var path = WriteCsv(csv);
        var parsed = new BrowserImportService().ParseFile(ImportBrowser.Chrome, path);
        Assert.False(parsed.Rows[0].IsDuplicateWithinFile);
        Assert.True(parsed.Rows[1].IsDuplicateWithinFile);
        Assert.False(parsed.Rows[2].IsDuplicateWithinFile);
    }

    // ── duplicate-key normalization ──

    [Theory]
    [InlineData("https://www.example.com/login", "https://example.com/x", true)]
    [InlineData("https://mail.example.com", "https://example.com", false)]
    [InlineData("http://example.com", "https://example.com", false)]
    [InlineData("https://example.com:8443", "https://example.com", false)]
    [InlineData("https://example.com/path?q=1", "https://example.com/other", true)]
    [InlineData("", "https://example.com", false)]
    public void Origin_Keys_Follow_Site_Rules(string left, string right, bool same)
    {
        var equal = string.Equals(DuplicateKey.OriginKey(left), DuplicateKey.OriginKey(right), StringComparison.Ordinal);
        Assert.Equal(same, equal);
    }

    [Fact]
    public void Username_Keys_Fold_Persian_Variants()
    {
        Assert.Equal(DuplicateKey.UsernameKey("علیي"), DuplicateKey.UsernameKey("علیی"));
        Assert.NotEqual(DuplicateKey.UsernameKey("a@x.com"), DuplicateKey.UsernameKey("b@x.com"));
    }

    [Fact]
    public void Origin_Display_Hides_Query_String()
    {
        Assert.Equal("example.com/account", DuplicateKey.OriginDisplay("https://user:pw@example.com/account?token=secret#frag"));
    }

    // ── hygiene ──

    [Fact]
    public void Source_File_Is_Never_Modified_And_No_Temp_Files_Are_Created()
    {
        var csv = "name,url,username,password,note\nsite,https://s.com,u,p,\n";
        var path = WriteCsv(csv);
        var before = File.ReadAllBytes(path);
        var siblingsBefore = Directory.GetFiles(_dir);

        var parsed = new BrowserImportService().ParseFile(ImportBrowser.Chrome, path);
        Assert.Single(parsed.Rows);

        Assert.Equal(before, File.ReadAllBytes(path));
        Assert.Equal(siblingsBefore, Directory.GetFiles(_dir));
        parsed.Dispose();
    }

    [Fact]
    public void Dispose_Clears_Secret_Row_References()
    {
        var path = WriteCsv("name,url,username,password,note\nsite,https://s.com,u,secret,\n");
        var parsed = new BrowserImportService().ParseFile(ImportBrowser.Chrome, path);
        var row = Assert.Single(parsed.Rows);
        Assert.Equal("secret", parsed.Secrets[row.RowNumber].Password);
        parsed.Dispose();
        Assert.Throws<ObjectDisposedException>(() => parsed.Secrets);
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); }
        catch (IOException) { }
    }
}
