using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using DBCD.Providers;
using MySqlConnector;
namespace AnoWowItemEditor;

public sealed record CatalogResult(List<CatalogItem> Items, int Overrides, string Server, TooltipData Tooltips);
public sealed class CatalogRepository
{
    internal const string Db2Folder = @"C:\AnoCore\AnoCore-Server\data\dbc\enUS";
    internal const string Database = "foreverclassic_hotfixes";
    private const uint SparseHash = 2442913102;
    private sealed record LoginInfo(string Server, uint Port, string User, string Password);
    public async Task<CatalogResult> LoadAsync()
    {
        var path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AnoCoreItemEditorStudio", "mariadb-login.dat");
        if (!File.Exists(path)) throw new InvalidOperationException("No saved MariaDB login found. Save your connection in the previous ItemEditor app, then retry.");
        var login = JsonSerializer.Deserialize<LoginInfo>(Encoding.UTF8.GetString(ProtectedData.Unprotect(await File.ReadAllBytesAsync(path), null, DataProtectionScope.CurrentUser))) ?? throw new InvalidOperationException("Saved MariaDB login could not be read.");
        var cs = new MySqlConnectionStringBuilder { Server=login.Server, Port=login.Port, UserID=login.User, Password=login.Password, Database=Database, ConnectionTimeout=8, SslMode=MySqlSslMode.Preferred };
        using var db = new MySqlConnection(cs.ConnectionString);
        await db.OpenAsync();
        // Obtain the actual TableHash from the installed file, validating the known build convention.
        using (var file = File.OpenRead(Path.Combine(Db2Folder,"ItemSparse.db2")))
        {
            var header=new byte[160]; file.ReadExactly(header);
            if(Encoding.ASCII.GetString(header,0,4)!="WDC5" || BitConverter.ToUInt32(header,152)!=SparseHash)
                throw new InvalidOperationException("Installed ItemSparse DB2 does not match the supported server build.");
        }
        var items = await Task.Run(LoadBaseline);
        // Match the server convention used by the previous editor: build 70450, then generic build 0.
        const string sql = "SELECT s.* FROM item_sparse s WHERE s.VerifiedBuild IN (70450,0) AND EXISTS (SELECT 1 FROM hotfix_data h WHERE h.TableHash=@hash AND h.RecordId=s.ID AND h.VerifiedBuild=s.VerifiedBuild AND h.Status=1) ORDER BY s.ID, CASE WHEN s.VerifiedBuild=70450 THEN 0 ELSE 1 END";
        var applied = new HashSet<int>();
        using(var cmd = new MySqlCommand(sql,db))
        {
            cmd.Parameters.AddWithValue("@hash",SparseHash);
            using var rows=await cmd.ExecuteReaderAsync();
            while(await rows.ReadAsync())
            {
                int id=Convert.ToInt32(rows["ID"]); if(!applied.Add(id)) continue;
                var fields=items.TryGetValue(id,out var original) ? new Dictionary<string,string>(original.Fields,StringComparer.OrdinalIgnoreCase) : new(StringComparer.OrdinalIgnoreCase);
                for(int i=0;i<rows.FieldCount;i++) fields[rows.GetName(i)]=Value(rows.GetValue(i));
                items[id]=new(fields,true);
            }
        }
        // Custom item identity provides type information for IDs absent from the original DB2.
        using(var cmd=new MySqlCommand("SELECT ID,ClassID,SubclassID FROM item ORDER BY CASE WHEN VerifiedBuild=70450 THEN 0 WHEN VerifiedBuild=0 THEN 1 ELSE 2 END",db))
        using(var rows=await cmd.ExecuteReaderAsync())
        {
            var seen=new HashSet<int>();
            while(await rows.ReadAsync()) { int id=Convert.ToInt32(rows["ID"]); if(!seen.Add(id)||!items.TryGetValue(id,out var item)) continue; if(!item.Fields.ContainsKey("ClassID")) { item.Fields["ClassID"]=Value(rows["ClassID"]); item.Fields["SubclassID"]=Value(rows["SubclassID"]); } }
        }
        var catalog=items.Values.Where(i=>i.Text("Display").Length>0 || i.Overridden).OrderBy(i=>i.Name,StringComparer.OrdinalIgnoreCase).ToList();
        var tooltips=await TooltipData.LoadAsync(db,catalog);
        return new(catalog,applied.Count,login.Server,tooltips);
    }
    private static string Value(object? value) => value is null or DBNull ? "" : Convert.ToString(value,CultureInfo.InvariantCulture) ?? "";
    private static Dictionary<int,CatalogItem> LoadBaseline()
    {
        var reader = new DBCD.DBCD(new FilesystemDBCProvider(Db2Folder),new GithubDBDProvider());
        var sparse=reader.Load("ItemSparse","7.3.5.70450");
        var items=new Dictionary<int,CatalogItem>();
        var aliases=new Dictionary<string,string> { ["Display_lang"]="Display",["Description_lang"]="Description" };
        foreach(var row in sparse.Values)
        {
            var fields=new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase);
            foreach(string col in sparse.AvailableColumns)
            {
                object? value=row[col];
                if(value is Array array)
                {
                    string name=col=="StatModifier_bonusStat" ? "StatModifierBonusStat" : col;
                    for(int i=0;i<array.Length;i++) fields[name+(i+1)]=Value(array.GetValue(i));
                }
                else fields[aliases.GetValueOrDefault(col) ?? col]=Value(value);
            }
            fields["ID"]=Convert.ToString(row.ID,CultureInfo.InvariantCulture)!;
            items[Convert.ToInt32(row.ID)]=new(fields);
        }
        var identity=reader.Load("Item","7.3.5.70450");
        foreach(var row in identity.Values) if(items.TryGetValue(Convert.ToInt32(row.ID),out var item))
        { item.Fields["ClassID"]=Value(row["ClassID"]); item.Fields["SubclassID"]=Value(row["SubclassID"]); }
        return items;
    }
}
