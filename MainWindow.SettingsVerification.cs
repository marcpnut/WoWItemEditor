using System.IO;
using System.Text;
using System.Text.Json;
namespace WoWItemEditor;
public partial class MainWindow
{
    private async Task VerifySettings()
    {
        try{
            void Check(bool ok,string reason){if(!ok)throw new InvalidOperationException(reason);}
            var original=AppSettings.Current;var sample=original with{User="verification",Password="local-roundtrip-test",WorldDatabase="test_world",HotfixDatabase="test_hotfix",CharacterDatabase="test_characters",BackupDatabase="test_history"};sample.Validate(false);
            var encrypted=AppSettings.Encode(sample);Check(!Encoding.UTF8.GetString(encrypted).Contains(sample.Password),"Settings must not contain plaintext password.");Check(AppSettings.Decode(encrypted)==sample,"Windows encrypted settings round-trip.");
            bool rejected=false;try{(sample with{WorldDatabase="world;DROP DATABASE test"}).Validate(false);}catch(InvalidOperationException){rejected=true;}Check(rejected,"Unsafe SQL schema identifiers rejected.");
            rejected=false;try{(sample with{Db2Folder=Path.Combine(Path.GetTempPath(),Guid.NewGuid().ToString())}).Validate();}catch(InvalidOperationException){rejected=true;}Check(rejected,"Missing DB2 folder rejected.");
            Check(AppSettings.Connection(sample,sample.WorldDatabase).Database=="test_world"&&AppSettings.Connection(sample,sample.WorldDatabase).Password==sample.Password,"Candidate connection uses candidate database and credentials.");Check(ReferenceEquals(original,AppSettings.Current),"Drafts do not alter current connection.");
            var settings=new SettingsWindow{Owner=this};settings.Show();await RenderEditorWindow(settings,"settings-preview");settings.Close();await File.WriteAllTextAsync("artifacts/settings-verification.json",JsonSerializer.Serialize(new{checks="DPAPI round-trip; encrypted password; unsafe schema and missing DB2 rejected; candidate connection; current settings unchanged; settings screen rendered",passed=true}));Close();
        }catch(Exception ex){Directory.CreateDirectory("artifacts");await File.WriteAllTextAsync("artifacts/verification-error.txt",ex.ToString());Environment.ExitCode=1;Close();}
    }
}
