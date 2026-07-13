using System;
using System.Collections;
using System.Collections.Specialized;
using System.Configuration;
using System.IO;
using System.Reflection;
using System.Xml;
using System.Xml.Linq;

namespace AgentHub.Common.Util
{
    /// <summary>
    /// Provides portable, persistent application settings.
    /// </summary>
    public class AppSettingsProvider : SettingsProvider, IApplicationSettingsProvider
    {

        private static XDocument GetXmlDoc()
        {
            // to deal with multiple settings providers accessing the same file, reload on every set or get request.
            XDocument xmlDoc = null;
            var initNew = false;
            if (File.Exists(ApplicationSettingsFile))
            {
                try
                {
                    xmlDoc = XDocument.Load(ApplicationSettingsFile);
                }
                catch { initNew = true; }
            }
            else
                initNew = true;
            if (initNew)
            {
                xmlDoc = new XDocument(new XElement("configuration", new XElement("userSettings", new XElement("Roaming"))));
            }
            return xmlDoc;
        }

        // 설정은 LocalAppData\AgentHub\app.config 에 보관한다. (기존엔 설치 폴더에 저장돼
        // 재설치·업데이트 시 통째로 교체되며 사라졌고, 그 결과 포트·인증서 비밀번호 등이 초기화됐다.)
        private static string ApplicationSettingsFile => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AgentHub", "app.config");

        // 설치 폴더에 있던 구버전 설정 파일(최초 1회 이관 원본).
        private static string LegacySettingsFile
        {
            get
            {
                var dir = Path.GetDirectoryName(Assembly.GetEntryAssembly()?.Location);
                return string.IsNullOrEmpty(dir) ? null : Path.Combine(dir, "app.config");
            }
        }

        // 설치 폴더의 옛 설정을 LocalAppData로 1회 이관(포트 등 기존 설정 유지). 대상이 이미 있으면 건너뜀.
        private static void MigrateLegacySettings()
        {
            try
            {
                var target = ApplicationSettingsFile;
                var dir = Path.GetDirectoryName(target);
                if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
                if (File.Exists(target)) return; // 이미 이관됨/신규 저장됨
                var legacy = LegacySettingsFile;
                if (legacy != null && File.Exists(legacy)) File.Copy(legacy, target);
            }
            catch { /* 이관 실패 시 기본값으로 시작 */ }
        }

        public override string ApplicationName { get => Assembly.GetExecutingAssembly().GetName().Name; set { } }

        public override string Name => "AppSettingsProvider";

        public override void Initialize(string name, NameValueCollection config)
        {
            if (string.IsNullOrEmpty(name)) name = "AppSettingsProvider";
            base.Initialize(name, config);
        }

        /// <summary>
        /// Applies this settings provider to each property of the given settings.
        /// </summary>
        /// <param name="settingsList">An array of settings.</param>
        public static void ApplyProvider(params ApplicationSettingsBase[] settingsList)
        {
            MigrateLegacySettings(); // 설치 폴더 → LocalAppData 1회 이관(포트 등 유지)
            foreach (var settings in settingsList)
            {
                var provider = new AppSettingsProvider();
                settings.Providers.Add(provider);
                foreach (SettingsProperty prop in settings.Properties)
                    prop.Provider = provider;
                settings.Reload();
            }
        }

        public SettingsPropertyValue GetPreviousVersion(SettingsContext context, SettingsProperty property)
        {
            throw new NotImplementedException();
        }

        public void Reset(SettingsContext context)
        {
            if (File.Exists(ApplicationSettingsFile))
                File.Delete(ApplicationSettingsFile);
        }

        public void Upgrade(SettingsContext context, SettingsPropertyCollection properties)
        { /* don't do anything here*/ }

        public override SettingsPropertyValueCollection GetPropertyValues(SettingsContext context, SettingsPropertyCollection collection)
        {
            var xmlDoc = GetXmlDoc();
            var values = new SettingsPropertyValueCollection();
            // iterate through settings to be retrieved
            foreach (SettingsProperty setting in collection)
            {
                var value = new SettingsPropertyValue(setting) { IsDirty = false };
                //Set serialized value to xml element from file. This will be deserialized by SettingsPropertyValue when needed.
                var loadedValue = GetXmlValue(xmlDoc, XmlConvert.EncodeLocalName((string)context["GroupName"]), setting);
                if (loadedValue != null)
                    value.SerializedValue = loadedValue;
                else value.PropertyValue = null;
                values.Add(value);
            }
            return values;
        }

        public override void SetPropertyValues(SettingsContext context, SettingsPropertyValueCollection collection)
        {
            var xmlDoc = GetXmlDoc();
            foreach (SettingsPropertyValue value in collection)
            {
                SetXmlValue(xmlDoc, XmlConvert.EncodeLocalName((string)context["GroupName"]), value);
            }
            try
            {
                var dir = Path.GetDirectoryName(ApplicationSettingsFile);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir)) Directory.CreateDirectory(dir);
                // Make sure that special chars such as '\r\n' are preserved by replacing them with char entities.
                using var writer = XmlWriter.Create(ApplicationSettingsFile, new XmlWriterSettings { NewLineHandling = NewLineHandling.Entitize, Indent = true });
                xmlDoc.Save(writer);
            }
            catch (Exception ex)
            {

            }
        }

        private static object GetXmlValue(XContainer xmlDoc, string scope, SettingsProperty prop)
        {
            object result;
            if (!IsUserScoped(prop))
                return null;
            //determine the location of the settings property
            var xmlSettings = xmlDoc.Element("configuration")?.Element("userSettings");
            xmlSettings = IsRoaming(prop) ? xmlSettings?.Element("Roaming") : xmlSettings?.Element("PC_" + Environment.MachineName);
            // retrieve the value or set to default if available
            if (xmlSettings?.Element(scope) != null && xmlSettings.Element(scope)?.Element(prop.Name) != null)
            {
                using (var reader = xmlSettings.Element(scope)?.Element(prop.Name)?.CreateReader())
                {
                    if (reader == null) return null;
                    reader.MoveToContent();
                    switch (prop.SerializeAs)
                    {
                        case SettingsSerializeAs.Xml:
                            result = reader.ReadInnerXml();
                            break;
                        case SettingsSerializeAs.Binary:
                            result = reader.ReadInnerXml();
                            result = Convert.FromBase64String(result as string);
                            break;
                        default:
                            result = reader.ReadElementContentAsString();
                            break;
                    }
                }
            }
            else
                result = prop.DefaultValue;
            return result;
        }

        private static void SetXmlValue(XContainer xmlDoc, string scope, SettingsPropertyValue value)
        {
            if (!IsUserScoped(value.Property)) return;
            //determine the location of the settings property
            var xmlSettings = xmlDoc.Element("configuration")?.Element("userSettings");
            var xmlSettingsLoc = IsRoaming(value.Property) ? xmlSettings?.Element("Roaming") : xmlSettings?.Element("PC_" + Environment.MachineName);
            // the serialized value to be saved
            XNode serialized;
            if (value.SerializedValue == null) serialized = new XText("");
            else switch (value.Property.SerializeAs)
            {
                case SettingsSerializeAs.Xml:
                    serialized = XElement.Parse((string)value.SerializedValue);
                    break;
                case SettingsSerializeAs.Binary:
                    serialized = new XText(Convert.ToBase64String((byte[])value.SerializedValue));
                    break;
                default:
                    serialized = new XText((string)value.SerializedValue);
                    break;
            }
            // check if setting already exists, otherwise create new
            if (xmlSettingsLoc == null)
            {
                xmlSettingsLoc = IsRoaming(value.Property) ? new XElement("Roaming") : new XElement("PC_" + Environment.MachineName);
                xmlSettingsLoc.Add(new XElement(scope,
                    new XElement(value.Name, serialized)));
                xmlSettings?.Add(xmlSettingsLoc);
            }
            else
            {
                var xmlScope = xmlSettingsLoc.Element(scope);
                if (xmlScope != null)
                {
                    var xmlElem = xmlScope.Element(value.Name);
                    if (xmlElem == null) xmlScope.Add(new XElement(value.Name, serialized));
                    else xmlElem.ReplaceAll(serialized);
                }
                else
                {
                    xmlSettingsLoc.Add(new XElement(scope, new XElement(value.Name, serialized)));
                }
            }
        }

        // Iterates through the properties' attributes to determine whether it's user-scoped or application-scoped.
        private static bool IsUserScoped(SettingsProperty prop)
        {
            foreach (DictionaryEntry d in prop.Attributes)
            {
                var a = (Attribute)d.Value;
                if (a is UserScopedSettingAttribute)
                    return true;
            }
            return false;
        }

        // Iterates through the properties' attributes to determine whether it's set to roam.
        private static bool IsRoaming(SettingsProperty prop)
        {
            foreach (DictionaryEntry d in prop.Attributes)
            {
                var a = (Attribute)d.Value;
                if (a is SettingsManageabilityAttribute)
                    return true;
            }
            return false;
        }
    }
}
