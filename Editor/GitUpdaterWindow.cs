using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEditor.PackageManager;
using UnityEditor.PackageManager.Requests;
using UnityEngine;
using PackageInfo = UnityEditor.PackageManager.PackageInfo;


namespace Toorah.GitUpdater.Editor
{

    public class GitUpdaterWindow : EditorWindow
    {
        [System.Serializable]
        private class Package
        {
            public string package;
            public string url;
            public string displayname;
            public string author;
            public string version;
        }

        #region Fields
        [SerializeField]
        private List<Package> m_packages = new List<Package>();
        private Vector2 m_scroll = Vector2.zero;
        string m_url;
        #endregion

        [MenuItem("Window/Git Package Updater")]
        private static void Open()
        {
            var win = GetWindow<GitUpdaterWindow>();
            win.titleContent.text = "Git Package Updater";
            win.Show();
        }

        private void OnEnable()
        {
            RefreshPackages();
            btnStyle = null;
        }

        GUIStyle btnStyle;
        private void OnGUI()
        {
            if (btnStyle == null)
            {
                btnStyle = new GUIStyle("button");
                btnStyle.richText = true;
                btnStyle.alignment = TextAnchor.MiddleLeft;
            }

            if (loadRequest || addRequest || removeRequest || reinstallURLs.Count > 0)
            {
                GUI.enabled = false;
            }
            else
            {
                GUI.enabled = true;
            }

            EditorGUILayout.BeginVertical();

            using (new GUILayout.HorizontalScope())
            {
                m_url = EditorGUILayout.TextField(m_url);
                if (GUILayout.Button("Add", GUILayout.ExpandWidth(false)))
                {
                    AddPendingPackage(m_url);
                    m_url = string.Empty;
                }
            }

            if (GUILayout.Button("Refresh"))
            {
                RefreshPackages();
            }
            if (GUILayout.Button("Update All"))
            {
                ReinstallAll();
            }

            if (reinstallURLs.Count > 0)
            {
                GUILayout.Label("Pending...");
                foreach (var url in reinstallURLs)
                {
                    GUILayout.Label(url, EditorStyles.miniLabel);
                }
            }

            m_scroll = GUILayout.BeginScrollView(
                scrollPosition: m_scroll,
                alwaysShowHorizontal: false,
                alwaysShowVertical: false,
                horizontalScrollbar: GUIStyle.none,
                verticalScrollbar: GUI.skin.verticalScrollbar,
                background: "Box"
            );

            m_scroll.x = 0;

            for (var i = 0; i < m_packages.Count; i++)
            {
                var p = m_packages[i];
                var btnContent = $"<b>{p.displayname}:</b>\t\t{p.version}\n<i><size=12>{p.package}</size></i>";

                if (GUILayout.Button(btnContent, btnStyle))
                {
                    ReinstallPackage(p);
                    break;
                }
            }

            GUILayout.EndScrollView();

            EditorGUILayout.EndVertical();
        }

        #region Methods


        public void RefreshPackages()
        {
            pmlr = Client.List();
            loadRequest = true;
            EditorUtility.DisplayProgressBar("Git Package Manager", "Fetching Package info", 0);
        }

        public void ReinstallAll()
        {
            foreach (var pack in m_packages)
            {
                AddPendingPackage(pack.url);
            }
            m_packages.Clear();
        }

        void RemovePackage(Package p)
        {
            m_packages.Remove(p);
            pmrr = Client.Remove(p.package);
            removeRequest = true;
        }

        public void AddPendingPackage(string url)
        {
            reinstallURLs.Add(url);
        }

        public void AddPackageFromURL(string url)
        {
            pmar = Client.Add(url);
            addRequest = true;
        }

        void ReinstallPackage(Package p)
        {
            var url = p.url;
            reinstallURLs.Add(url);
            m_packages.Remove(p);
            AddPackageFromURL(url);

            //reinstallURLs.Add(p.url);
            //RemovePackage(p);
        }


        public List<string> reinstallURLs = new List<string>();
        int wait = 0;

        ListRequest pmlr;
        bool loadRequest;
        AddRequest pmar;
        bool addRequest;
        RemoveRequest pmrr;
        bool removeRequest;

        bool needsRefresh;

        private void Update()
        {
            if (wait-- > 0)
            {
                return;
            }

            if (removeRequest)
            {
                if (pmrr.IsCompleted)
                {
                    wait += 10;
                    removeRequest = false;
                    switch (pmrr.Status)
                    {
                        default:
                        case StatusCode.Failure:
                            if (pmrr.Error != null)
                                Debug.LogError($"{{{pmrr.Error.errorCode}}} {pmrr.Error.message}");
                            break;
                    }
                }
            }
            else if (addRequest)
            {
                if (pmar.IsCompleted)
                {
                    addRequest = false;
                    wait += 10;
                    switch (pmar.Status)
                    {
                        case StatusCode.Success:
                            var p = pmar.Result;
                            CreatePackageFromInfo(p);
                            break;
                        default:
                        case StatusCode.Failure:
                            if (pmar.Error != null)
                            {
                                Debug.LogError($"{{{pmar.Error.errorCode}}} {pmar.Error.message}");
                                if(reinstallURLs.Count > 0)
                                {
                                    var packUrl = ExtractPackageURLFromError(pmar.Error.message);
                                    reinstallURLs.Remove(packUrl);
                                }
                            }
                            break;
                    }
                }
            }
            else if (loadRequest)
            {
                if (pmlr.IsCompleted)
                {
                    wait += 10;
                    loadRequest = false;
                    EditorUtility.ClearProgressBar();
                    switch (pmlr.Status)
                    {
                        case StatusCode.Success:
                            m_packages.Clear();
                            var result = pmlr.Result;
                            foreach (var pack in result)
                            {
                                CreatePackageFromInfo(pack);
                            }
                            break;
                        default:
                        case StatusCode.Failure:
                            if (pmlr.Error != null)
                                Debug.LogError($"{{{pmlr.Error.errorCode}}} {pmlr.Error.message}");
                            break;
                    }

                }
            }
            else if (reinstallURLs.Count > 0)
            {
                var r = reinstallURLs[0];
                AddPackageFromURL(r);
                wait += 10;
                needsRefresh = true;
            }
            else if (needsRefresh)
            {
                RefreshPackages();
                needsRefresh = false;
            }
        }



        void CreatePackageFromInfo(PackageInfo pi)
        {
            var id = pi.packageId.Split('@');
            if (id[0].Contains("com.unity."))
                return;

            var path = Path.Combine(pi.assetPath, "package.json");
            if (File.Exists(path))
            {
                var packagejson = File.ReadAllText(path);
                var dependency = GetDependencies(packagejson);
                if(!string.IsNullOrEmpty(dependency))
                {
                    var dependencies = dependency.Trim().Split(',');
                    Debug.Log($"Found Dependencies for {id[0]}:");
                    foreach(var dep in dependencies)
                    {
                        var deps = dep.Replace("\":", ";").Replace("\"", "").Split(';');
                        Debug.Log($"--> {deps[0]} @ {deps[1]}");
                        AddPendingPackage(deps[1]);
                    }
                }
            }

            var package = new Package
            {
                package = id[0],
                url = id[1],
                displayname = pi.displayName,
                author = pi.author != null ? pi.author.name : string.Empty,
                version = pi.version
            };
            if(m_packages.Find(x => x.url == package.url) == null)
                m_packages.Add(package);

            if (reinstallURLs.Count > 0)
            {
                if (reinstallURLs.Contains(package.url))
                    reinstallURLs.Remove(package.url);
            }
        }
        #endregion


        string GetDependencies(string t)
        {
            Regex reg = new Regex("\"gitdependencies\":[\\s\\S]*?{([\\s\\S]*?)}");
            var match = reg.Match(t);
            if (match.Groups == null || match.Groups.Count == 0)
                return string.Empty;

            return match.Groups[match.Groups.Count - 1].Value;
        }

        string ExtractPackageURLFromError(string error)
        {
            Regex reg = new Regex(@"\[(.*)\]");
            var match = reg.Match(error);
            if (match.Groups == null || match.Groups.Count == 0)
                return null;
            return match.Groups[match.Groups.Count - 1].Value;
        }
    }
}