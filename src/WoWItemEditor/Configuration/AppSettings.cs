using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using MySqlConnector;
namespace WoWItemEditor;

internal sealed record EditorSettings
{
    public string Server {get;init;}="localhost";
    public uint Port {get;init;}=3306;
    public string User {get;init;}="";
    public string Password {get;init;}="";
    public string HotfixDatabase {get;init;}="foreverclassic_hotfixes";
    public string WorldDatabase {get;init;}="foreverclassic_world";
    public string CharacterDatabase {get;init;}="foreverclassic_characters";
    public string BackupDatabase {get;init;}="wow_item_editor_backup";
    public string NpcCacheDatabase {get;init;}="wow_item_editor_npc_cache";
    public string Db2Folder {get;init;}="";
    public string MapFolder {get;init;}="";
    public string ClientFolder {get;init;}="";
    public string MariaDbFolder {get;init;}="";
    public string SslMode {get;init;}="Preferred";
    public uint ConnectionTimeout {get;init;}=8;
    public bool AllowAssetDownloads {get;init;}=true;
    internal void Validate(bool folders=true)
    {
        if(string.IsNullOrWhiteSpace(Server)||string.IsNullOrWhiteSpace(User)||Port is 0 or >65535||ConnectionTimeout is <1 or >120)throw new InvalidOperationException("Enter a host, user, port 1–65535 and timeout 1–120 seconds.");
        var names=new[]{HotfixDatabase,WorldDatabase,CharacterDatabase,BackupDatabase,NpcCacheDatabase};
        if(names.Any(n=>!Regex.IsMatch(n,"^[A-Za-z0-9_]{1,64}$"))||names.Distinct(StringComparer.OrdinalIgnoreCase).Count()!=5)throw new InvalidOperationException("Use five different database names containing only letters, digits and underscores (maximum 64 characters).");
        if(!Enum.TryParse<MySqlSslMode>(SslMode,out _))throw new InvalidOperationException("Choose a supported TLS mode.");
        if(!folders)return;
        if(!File.Exists(Path.Combine(Db2Folder,"ItemSparse.db2")))throw new InvalidOperationException("Choose the extracted DB2 folder containing ItemSparse.db2 (usually data/dbc/enUS).");
        foreach(var path in new[]{MapFolder,MariaDbFolder})if(path.Length>0&&!Directory.Exists(path))throw new InvalidOperationException("Folder does not exist: "+path);
        if(ClientFolder.Length>0&&!File.Exists(Path.Combine(ClientFolder,".build.info")))throw new InvalidOperationException("Choose the WoW installation root containing .build.info.");
    }
}
internal static class AppSettings
{
    internal static readonly string Folder=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"WoWItemEditor");
    internal static string FilePath=>Path.Combine(Folder,"settings.dat");
    internal static EditorSettings Current {get;private set;}=Load();
    internal static string? LoadError {get;private set;}
    internal static bool Configured {get{try{Current.Validate();return true;}catch{return false;}}}
    private static EditorSettings Load()
    {
        try{
            if(File.Exists(FilePath))return Decode(File.ReadAllBytes(FilePath));
            string legacy=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"AnoCoreItemEditorStudio","mariadb-login.dat");
            if(File.Exists(legacy)){
                var login=JsonSerializer.Deserialize<EditorSettings>(ProtectedData.Unprotect(File.ReadAllBytes(legacy),null,DataProtectionScope.CurrentUser))??new();
                string data=@"C:\AnoCore\AnoCore-Server\data";string client=@"C:\Program Files (x86)\World of Warcraft";string preferences=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"AnoWowItemEditor","model-viewer.json");
                if(File.Exists(preferences))client=JsonSerializer.Deserialize<Dictionary<string,string>>(File.ReadAllText(preferences))?.GetValueOrDefault("ClientFolder")??client;
                var migrated=login with{BackupDatabase="anocore_studio_backup",Db2Folder=Path.Combine(data,"dbc","enUS"),MapFolder=Path.Combine(data,"maps"),ClientFolder=Directory.Exists(client)?client:""};Save(migrated);return migrated;
            }
        }catch{LoadError="Saved settings could not be read. Enter the connection and folders again.";}
        return new();
    }
    internal static byte[] Encode(EditorSettings settings)=>ProtectedData.Protect(JsonSerializer.SerializeToUtf8Bytes(settings),null,DataProtectionScope.CurrentUser);
    internal static EditorSettings Decode(byte[] bytes)=>JsonSerializer.Deserialize<EditorSettings>(ProtectedData.Unprotect(bytes,null,DataProtectionScope.CurrentUser))??throw new InvalidDataException("Settings are empty.");
    internal static void Save(EditorSettings settings){settings.Validate();Directory.CreateDirectory(Folder);string temporary=FilePath+".tmp";File.WriteAllBytes(temporary,Encode(settings));File.Move(temporary,FilePath,true);}
    internal static void ApplyInitial(EditorSettings settings)=>Current=settings;
    internal static MySqlConnectionStringBuilder Connection(EditorSettings settings,string database)=>new(){Server=settings.Server,Port=settings.Port,UserID=settings.User,Password=settings.Password,Database=database,ConnectionTimeout=settings.ConnectionTimeout,SslMode=Enum.Parse<MySqlSslMode>(settings.SslMode),AllowUserVariables=false};
}
