using System.IO;
using OuterRim.Core.Content;
using UnityEngine;

namespace OuterRim.Client
{
    /// <summary>
    /// Finds the game data and loads the base pack once. In the Editor the data is read from the repository's
    /// data/ folder, found above the project; copying it into builds comes with the session tasks (PLAN T3.9).
    /// </summary>
    public static class ContentLocator
    {
        public const string BaseManifest = "data/packs/base/pack.json";

        private static GameContent _base;

        public static GameContent Base
        {
            get
            {
                if (_base == null)
                {
                    string root = RepositoryRoot();
                    _base = PackLoader.Load(p => File.ReadAllText(Path.Combine(root, p)), new[] { BaseManifest });
                }
                return _base;
            }
        }

        /// <summary>The folder holding data/, searched upward from the project's Assets folder.</summary>
        public static string RepositoryRoot()
        {
            for (var dir = new DirectoryInfo(Application.dataPath); dir != null; dir = dir.Parent)
            {
                if (File.Exists(Path.Combine(dir.FullName, BaseManifest))) return dir.FullName;
            }
            throw new FileNotFoundException(BaseManifest + " not found above " + Application.dataPath);
        }
    }
}
