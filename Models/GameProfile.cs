using System;
using System.IO;
using System.Xml.Serialization;

namespace SteamCmdWeb.Models
{
    [Serializable]
    public class GameProfile
    {
        public string ProfileName { get; set; }
        public string InstallDir { get; set; }
        public string EncryptedUsername { get; set; }
        public string EncryptedPassword { get; set; }
        public string AppID { get; set; }
        public string Arguments { get; set; }

        public GameProfile()
        {
            ProfileName = "";
            InstallDir = "";
            EncryptedUsername = "";
            EncryptedPassword = "";
            AppID = "";
            Arguments = "";
        }

        public void SaveToFile(string filePath)
        {
            XmlSerializer serializer = new XmlSerializer(typeof(GameProfile));
            using (StreamWriter writer = new StreamWriter(filePath))
            {
                serializer.Serialize(writer, this);
            }
        }

        public static GameProfile LoadFromFile(string filePath)
        {
            if (!File.Exists(filePath)) return null;
            XmlSerializer serializer = new XmlSerializer(typeof(GameProfile));
            using (StreamReader reader = new StreamReader(filePath))
            {
                return (GameProfile)serializer.Deserialize(reader);
            }
        }
    }
}