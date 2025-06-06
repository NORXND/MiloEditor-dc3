using MiloLib;

namespace ImMilo;

using System.Text;
using IconFonts;
using ImGuiNET;
using ImMilo.ImGuiUtils;
using MiloLib.Assets;
using MiloLib.Assets.Band;
using MiloLib.Utils;
using MiloLib.Utils.Conversion;
using TinyDialogsNet;
using Vector2 = System.Numerics.Vector2;
using Vector4 = System.Numerics.Vector4;


public partial class Program
{

    // Track selected entries for multi-selection
    private static HashSet<DirectoryMeta.Entry> selectedEntries = new HashSet<DirectoryMeta.Entry>();
    private static bool isMultiSelecting = false;

    private static string PopupFlag = "";

    private static SearchWindow FindRefsSearch = new("Find References")
    {
        EnableDirectories = false,
        EnableEntries = false,
        Type = SearchWindow.SearchType.Exact
    };

    static bool BeginPopupModalDirNode(string label, ImGuiWindowFlags flags)
    {
        if (PopupFlag == label)
        {
            ImGui.OpenPopup(label);
            PopupFlag = "";
        }
        return ImGui.BeginPopupModal(label, flags);
    }

    static void OpenPopupDirNode(string label)
    {
        PopupFlag = label;
    }

    static void DirNode(DirectoryMeta dir, int id = 0, bool root = false)
    {
        var iterId = 0;
        DirNode(dir, ref iterId, id, root);
    }

    static async void MergeDirectory(DirectoryMeta dirEntry, string path)
    {
        bool? alreadyExistsOverride = null;
        MiloFile externalMiloScene = new MiloFile(path);
        if (await ShowConfirmPrompt("Would you like to fix parentObj references in this merge? (If unsure, click Yes)"))
        {
            var searcher = new SearchWindow("Ref Fixer")
            {
                TargetScene = externalMiloScene,
                Query = externalMiloScene.dirMeta.name
            };

            await searcher.UpdateQuery();

            foreach (var result in searcher.Results)
            {
                if (result.Result is SearchWindow.FieldBreadcrumb fieldBreadcrumb)
                {
                    if (fieldBreadcrumb.Target.Name == "target" || fieldBreadcrumb.Target.Name == "parentObj")
                    {
                        // Update all the references to the new directory's name
                        fieldBreadcrumb.Target.SetValue(fieldBreadcrumb.Parent, dirEntry.name);
                    }
                }
            }
        }
        ObjectDir dir = (ObjectDir)dirEntry.directory;

        // Iterate through entries in the external scene to be merged
        foreach (DirectoryMeta.Entry mergeEntry in externalMiloScene.dirMeta.entries)
        {
            bool entryMerged = false; // Flag to track if we have either merged an entry or added a new entry

            // Iterate through the existing entries in the current scene
            for (int i = 0; i < dirEntry.entries.Count; i++)
            {
                DirectoryMeta.Entry currentEntry = dirEntry.entries[i];

                if (mergeEntry.name.value == currentEntry.name.value)
                {
                    bool shouldOverwrite = false;
                    if (alreadyExistsOverride == null)
                    {
                        var promptInput =
                            await ShowChoosePrompt(
                                $"An entry with the name {currentEntry.name.value} already exists. Do you want to overwrite it?",
                                "Merge Conflict", "Yes", "No", "Yes to All", "No to All");
                        switch (promptInput)
                        {
                            case "Yes":
                                shouldOverwrite = true;
                                break;
                            case "No":
                                shouldOverwrite = false;
                                break;
                            case "Yes to All":
                                shouldOverwrite = true;
                                alreadyExistsOverride = true;
                                break;
                            case "No to All":
                                shouldOverwrite = false;
                                alreadyExistsOverride = false;
                                break;
                        }
                    }
                    else
                    {
                        shouldOverwrite = alreadyExistsOverride.Value;
                    }
                    if (shouldOverwrite)
                    {
                        dirEntry.entries[i].obj = mergeEntry.obj;
                    }
                    entryMerged = true;
                    break; // Entry has been processed, no need to check other entries.
                }
            }

            if (!entryMerged)
            {
                // Add a new entry from the external file
                dirEntry.entries.Add(mergeEntry);
            }
        }

        foreach (DirectoryMeta externalSubDir in ((ObjectDir)externalMiloScene.dirMeta.directory).inlineSubDirs)
        {
            bool subDirMerged = false;

            for (int i = 0; i < dir.inlineSubDirs.Count; i++)
            {
                DirectoryMeta currentSubDir = dir.inlineSubDirs[i];

                if (externalSubDir.name.value == currentSubDir.name.value)
                {
                    if (await ShowConfirmPrompt($"An inline subdirectory with the name {currentSubDir.name.value} already exists. Do you want to overwrite it?"))
                    {
                        dir.inlineSubDirs[i] = externalSubDir;
                    }
                    subDirMerged = true;
                    break;
                }
            }

            if (!subDirMerged)
            {
                dir.inlineSubDirs.Add(externalSubDir);
                dir.inlineSubDirNames.Add($"{externalSubDir.name.value}.milo");
                dir.referenceTypes.Add(ObjectDir.ReferenceType.kInlineCached);
                dir.referenceTypesAlt.Add(ObjectDir.ReferenceType.kInlineCached);
            }
        }
    }

    static async void PromptDuplicateEntry(DirectoryMeta dir, DirectoryMeta.Entry entry)
    {

        var newName = await ShowTextPrompt("New name", "Duplicate", entry.name.value);

        if (newName == null)
        {
            return;
        }

        try
        {
            var newEntry = DuplicateEntry(entry, dir);

            newEntry.name = newName;
            dir.entries.Add(newEntry);
        }
        catch (Exception e)
        {
            OpenErrorModal(e, "Failed to duplicate asset.");
        }
    }

    static async void PromptRenameEntry(DirectoryMeta.Entry entry)
    {
        var newName = await ShowTextPrompt("New name", "Rename", entry.name.value);

        if (newName == null)
        {
            return;
        }

        entry.name = newName;
        if (entry.dir != null)
        {
            entry.dir.name = newName;
        }
    }

    class AssetTypePrompt : Prompt<string?>
    {
        private int curType = 0;
        readonly string[] assetTypes = ["Object", "Tex", "Group", "Trans", "BandSongPref", "Sfx", "BandCharDesc", "HamMove", "SkeletonClip", "CharClip", "DancerSequence", "RndPropAnim", "MoveDir"];

        public AssetTypePrompt()
        {
            Title = "Import Asset";
        }

        public override void Show()
        {
            if (BeginModal())
            {
                ImGui.Combo("Asset type", ref curType, assetTypes, assetTypes.Length);

                if (ImGui.Button("OK"))
                {
                    Complete(assetTypes[curType]);
                }
                ImGui.SameLine();
                if (ImGui.Button("Cancel"))
                {
                    Complete(null);
                }
                ImGui.EndPopup();
            }
        }
    }
    static async void PromptImportAsset(DirectoryMeta dir)
    {
        var (canceled, paths) = TinyDialogs.OpenFileDialog("Import Asset", "", false);
        if (!canceled)
        {
            var path = paths.First();
            // special handling of certain asset types

            // detect .prefab file
            if (Path.GetExtension(path) == ".prefab")
            {
                // read file
                BandCharDesc desc = NautilusInterop.ToBandCharDesc(File.ReadAllText(path));
                // add the BandCharDesc to the MiloFile
                currentScene.dirMeta.entries.Add(new DirectoryMeta.Entry("BandCharDesc",
                    "prefab_" + Path.GetFileNameWithoutExtension(path), desc));
            }
            else
            {
                string? assetType = null;
                switch (Path.GetExtension(path))
                {
                    case ".move":
                        assetType = "HamMove";
                        break;
                    case ".tex":
                        assetType = "Tex";
                        break;
                    case ".seq":
                        assetType = "DancerSequence";
                        break;
                    case ".clp":
                        assetType = "SkeletonClip";
                        break;
                    case ".anim":
                        assetType = "RndPropAnim";
                        break;
                    default:
                        assetType = await ShowGenericPrompt(new AssetTypePrompt());
                        break;
                }

                if (assetType == null)
                {
                    return;
                }

                try
                {
                    ImportAsset(dir, path, assetType);
                }
                catch (Exception e)
                {
                    OpenErrorModal(e, "Failed to import asset.");
                }

            }
        }
    }

    static async void PromptExportDirectory(DirectoryMeta dir)
    {
        var file = new MiloFile(dir);
        var saveSettings = await ShowSavePrompt(currentScene);
        if (saveSettings == null)
        {
            return;
        }

        var (canceled, path) = TinyDialogs.SaveFileDialog("Export Asset", currentScene.filePath, MiloFileFilter);
        if (!canceled)
        {
            try
            {
                file.Save(path, saveSettings.compressionType, 0x810, Endian.LittleEndian, saveSettings.endianness);
                ShowNotifyPrompt($"Exported to {path} successfully.", "Export Directory");
            }
            catch (Exception e)
            {
                OpenErrorModal(e, "Failed to export directory.");
            }
        }
    }

    class NewAssetPrompt : Prompt<(string, string)?>
    {
        private string newName = "";
        private int curType = 0;
        readonly string[] assetTypes = ["ObjectDir", "MoveDir"];

        public NewAssetPrompt()
        {
            Title = "Create New Asset";
        }

        public override void Show()
        {
            if (BeginModal())
            {
                ImGui.InputText("Name", ref newName, 32);
                ImGui.Combo("Asset type", ref curType, assetTypes, assetTypes.Length);

                ImGui.Separator();
                if (ImGui.Button("OK"))
                {
                    Complete((newName, assetTypes[curType]));
                }
                ImGui.SameLine();
                if (ImGui.Button("Cancel"))
                {
                    Complete(null);
                }
                ImGui.EndPopup();
            }
        }
    }

    static async void PromptCreateNewAsset(DirectoryMeta dir)
    {
        var promptInput = await ShowGenericPrompt(new NewAssetPrompt());
        if (promptInput != null)
        {
            var (newName, assetType) = promptInput.Value;

            try
            {
                Object newObj;
                switch (assetType)
                {
                    case "ObjectDir":
                        newObj = new ObjectDir(2, 0); // Using common revision values
                        break;
                    case "MoveDir":
                        newObj = new MiloLib.Assets.Ham.MoveDir(2, 0); // Using common revision values
                        break;
                    default:
                        throw new NotImplementedException($"Asset type {assetType} not implemented for creation");
                }

                // Create a new entry and add it to the directory
                var entry = new DirectoryMeta.Entry(assetType, newName, newObj);
                dir.entries.Add(entry);
            }
            catch (Exception e)
            {
                OpenErrorModal(e, $"Failed to create new {assetType}.");
            }
        }
    }

    class AddSubDirPrompt : Prompt<(string, string)?>
    {
        private string newName = "";
        private static readonly List<string> Types = ["ObjectDir", "WorldDir", "RndDir", "PanelDir"];
        private int curType = 0;

        public AddSubDirPrompt()
        {
            Title = "Add Inlined Subdirectory";
        }

        public override void Show()
        {
            if (BeginModal())
            {
                ImGui.InputText("Name", ref newName, 32);
                ImGui.Combo("Type", ref curType, Types.ToArray(), Types.Count);

                ImGui.Separator();
                if (ImGui.Button("OK"))
                {
                    Complete((newName, Types[curType]));
                }
                ImGui.SameLine();
                if (ImGui.Button("Cancel"))
                {
                    Complete(null);
                }
                ImGui.EndPopup();
            }
        }
    }

    static async void PromptAddSubdir(DirectoryMeta dirEntry)
    {
        ObjectDir dir = (ObjectDir)dirEntry.directory;

        var promptInput = await ShowGenericPrompt(new AddSubDirPrompt());
        if (promptInput != null)
        {
            var (newName, newType) = promptInput.Value;
            var newDir = DirectoryMeta.New(newType, newName, 27, 25); // this is how it's done in MiloEditor
            dir.inlineSubDirs.Add(newDir);
            dir.inlineSubDirNames.Add($"{newName}.milo");
            dir.referenceTypes.Add(ObjectDir.ReferenceType.kInlineCached);
            dir.referenceTypesAlt.Add(ObjectDir.ReferenceType.kInlineCached);
        }
    }

    static void FindReferencesMenu(string target)
    {
        if (ImGui.BeginMenu(FontAwesome5.Search + "  Find References"))
        {
            if (ImGui.IsWindowAppearing())
            {
                FindRefsSearch.TargetScene = currentScene;
                FindRefsSearch.Query = target;
                FindRefsSearch.UpdateQuery();
            }

            if (ImGui.TextLink("Open in Search Window"))
            {
                SearchWindow.mainWindowOpen = true;
                SearchWindow.mainWindow.TargetScene = currentScene;
                SearchWindow.mainWindow.Query = target;
                SearchWindow.mainWindow.Results = new List<SearchWindow.SearchResult>(FindRefsSearch.Results);
                SearchWindow.mainWindow.EnableDirectories = false;
                SearchWindow.mainWindow.EnableEntries = false;
                SearchWindow.mainWindow.EnableFields = true;
                SearchWindow.mainWindow.Type = SearchWindow.SearchType.Exact;
                ImGui.CloseCurrentPopup();
            }
            FindRefsSearch.Draw(true);
            ImGui.EndMenu();
        }
    }

    static void DirNode(DirectoryMeta dir, ref int iterId, int id = 0, bool root = false, DirectoryMeta parent = null,
        bool inlined = false, DirectoryMeta.Entry? thisEntry = null, bool useEntryContextMenu = false)
    {
        if (Settings.Editing.CompactSceneTree)
        {
            ImGui.PushStyleVar(ImGuiStyleVar.ItemSpacing, new Vector2(ImGui.GetStyle().ItemSpacing.X, 0f));
        }
        var filterActive = filter != "";
        ImGui.PushID(id);
        var flags = ImGuiTreeNodeFlags.OpenOnArrow | ImGuiTreeNodeFlags.OpenOnDoubleClick
                                                   | ImGuiTreeNodeFlags.SpanFullWidth;
        if (root || filterActive)
        {
            flags |= ImGuiTreeNodeFlags.DefaultOpen;
        }

        if (filterActive)
        {
            ImGui.SetNextItemStorageID(ImGui.GetID("filtering"));
        }

        if (viewingObject == dir.directory)
        {
            flags |= ImGuiTreeNodeFlags.Selected;
        }

        var iconSize = Settings.Loaded.ScaledIconSize;
        var framePadding = ImGui.GetStyle().FramePadding;

        var drawList = ImGui.GetWindowDrawList();
        var treeLineColor = ImGui.GetColorU32(ImGuiCol.Text);
        treeLineColor = (0x00ffffff & treeLineColor) | 0x80000000;

        Stack<Vector2> traceStack = new();

        // Adds to the trace stack, run before a tree directory is drawn
        void PushTrace()
        {
            var cursor = ImGui.GetCursorScreenPos();
            var lineX = cursor.X + iconSize / 2f + framePadding.X;
            var lineY = cursor.Y + ImGui.GetFrameHeight() - ImGui.GetStyle().ItemSpacing.Y - framePadding.Y;
            traceStack.Push(new Vector2(lineX, lineY));
        }

        // Pops from the trace stack, and draws the line
        void PopTrace()
        {
            var line = traceStack.Pop();
            var stylePtr = ImGui.GetStyle();
            drawList.AddLine(line,
                new Vector2(line.X, (ImGui.GetCursorScreenPos().Y - stylePtr.ItemSpacing.Y) - ImGui.GetFrameHeight() / 2f + stylePtr.FramePadding.Y + 1f), treeLineColor);
        }

        void DrawChildLine(bool dir, Vector2 cursor)
        {
            var lineEndX = cursor.X + framePadding.X;
            if (!dir)
            {
                lineEndX += iconSize;
            }

            var yOffset = iconSize / 2f;
            drawList.AddLine(new Vector2(traceStack.Peek().X + 1, cursor.Y + yOffset), new Vector2(lineEndX, cursor.Y + yOffset),
                treeLineColor);
        }

        void DirectoryContextMenu(ref int iterId)
        {
            if (useEntryContextMenu)
            {
                ItemContextMenu(thisEntry, ref iterId);
                return;
            }

            ImGui.PushStyleVar(ImGuiStyleVar.Alpha, 1f);
            ImGui.PushStyleVar(ImGuiStyleVar.ItemSpacing, ImGui.GetStyle().FramePadding);
            if (ImGui.BeginPopupContextItem())
            {
                ImGui.PushFont(Util.mainFont);
                if (parent != null)
                {
                    if (ImGui.MenuItem(FontAwesome5.TrashAlt + "  Delete Directory"))
                    {
                        if (viewingObject == dir.directory)
                        {
                            viewingObject = null;
                        }

                        if (inlined)
                        {
                            var objDir = (ObjectDir)parent.directory;
                            objDir.inlineSubDirs.Remove(dir);
                        }
                        else
                        {
                            parent.entries.Remove(thisEntry);
                        }

                        iterId--;
                    }

                    ImGui.MenuItem(FontAwesome5.Clone + "  Duplicate Directory", "", false, false);
                }

                if (ImGui.MenuItem(FontAwesome5.Edit + "  Rename Directory"))
                {
                    var prompt = async () =>
                    {
                        var newName = await ShowTextPrompt("New name", "Rename Directory", dir.name);
                        if (newName != null)
                        {
                            dir.name = newName;
                            if (thisEntry != null)
                            {
                                thisEntry.name = dir.name;
                            }
                        }
                    };
                    prompt();
                }
                if (ImGui.MenuItem(FontAwesome5.CodeMerge + "  Merge Directory"))
                {
                    var (canceled, path) = TinyDialogs.OpenFileDialog("Merge Directory", currentScene.filePath, false,
                        MiloFileFilter);

                    if (!canceled)
                    {
                        MergeDirectory(dir, path.First());
                    }
                }

                if (ImGui.MenuItem(FontAwesome5.Share + "  Export Directory"))
                {
                    PromptExportDirectory(dir);
                }

                if (ImGui.MenuItem(FontAwesome5.PlusSquare + "  Add Inlined Directory"))
                {
                    PromptAddSubdir(dir);
                }
                ImGui.Separator();
                if (ImGui.MenuItem(FontAwesome5.FileImport + "  Import Asset"))
                {
                    PromptImportAsset(dir);
                }

                if (ImGui.MenuItem(FontAwesome5.PlusCircle + "  New Asset"))
                {
                    PromptCreateNewAsset(dir);
                }

                FindReferencesMenu(dir.name);

                ImGui.PopFont();
                ImGui.EndPopup();
            }
            ImGui.PopStyleVar();
            ImGui.PopStyleVar();
        }

        void ItemContextMenu(DirectoryMeta.Entry entry, ref int curIndex)
        {
            ImGui.PushStyleVar(ImGuiStyleVar.Alpha, 1f);
            ImGui.PushStyleVar(ImGuiStyleVar.ItemSpacing, ImGui.GetStyle().FramePadding);
            if (ImGui.BeginPopupContextItem())
            {
                ImGui.PushFont(Util.mainFont);

                // Check if we have multiple items selected
                bool hasMultipleSelected = selectedEntries.Count > 1 && selectedEntries.Contains(entry);

                if (hasMultipleSelected)
                {
                    if (ImGui.MenuItem(FontAwesome5.TrashAlt + $"  Delete Selected ({selectedEntries.Count} items)"))
                    {
                        // Create a copy of the selected entries to avoid collection modification issues
                        var entriesToDelete = new List<DirectoryMeta.Entry>(selectedEntries);

                        foreach (var selectedEntry in entriesToDelete)
                        {
                            if (viewingObject == selectedEntry.obj)
                            {
                                viewingObject = null;
                            }

                            // Find the directory containing this entry
                            if (dir.entries.Contains(selectedEntry))
                            {
                                dir.entries.Remove(selectedEntry);
                            }
                        }

                        // Clear selection after deletion
                        selectedEntries.Clear();
                        curIndex = -1; // Force reindex
                    }
                }
                else
                {
                    if (ImGui.MenuItem(FontAwesome5.TrashAlt + "  Delete Asset"))
                    {
                        if (viewingObject == entry.obj)
                        {
                            viewingObject = null;
                        }

                        dir.entries.Remove(entry);
                        selectedEntries.Remove(entry);
                        curIndex--;
                    }
                }

                if (!hasMultipleSelected)
                {
                    if (ImGui.MenuItem(FontAwesome5.Clone + "  Duplicate Asset"))
                    {
                        PromptDuplicateEntry(dir, entry);
                    }

                    if (ImGui.MenuItem(FontAwesome5.Edit + "  Rename Asset"))
                    {
                        PromptRenameEntry(entry);
                    }

                    ImGui.Separator();
                    if (ImGui.MenuItem(FontAwesome5.Share + "  Extract Asset"))
                    {
                        var (canceled, path) = TinyDialogs.SaveFileDialog("Extract Asset", entry.name);

                        if (!canceled)
                        {
                            File.WriteAllBytes(path, entry.objBytes.ToArray());
                        }
                    }

                    if (ImGui.MenuItem(FontAwesome5.Recycle + "  Replace Asset"))
                    {
                        var (canceled, paths) = TinyDialogs.OpenFileDialog("Replace Asset", "", false);
                        if (!canceled)
                        {
                            var backupSuccess = false;
                            var backupStream = new MemoryStream();
                            try
                            {
                                // Backup the asset in case of an error while reading
                                entry.obj.GetType().GetMethod("Write").Invoke(entry.obj,
                                    [new EndianWriter(backupStream, currentScene.endian), false, dir, entry]);
                                backupSuccess = true;
                                var path = paths.First();

                                byte[] fileBytes = File.ReadAllBytes(path);
                                entry.objBytes = fileBytes.ToList();
                                // Use reflection to call the read method as the comment below only calls the Object's Read()
                                entry.obj.GetType().GetMethod("Read").Invoke(entry.obj, [new EndianReader(new MemoryStream(fileBytes), currentScene.endian), false, dir, entry]);
                            }
                            catch (Exception e)
                            {
                                OpenErrorModal(e, "Cannot replace asset.");
                                if (backupSuccess)
                                {
                                    backupStream.Seek(0, SeekOrigin.Begin);
                                    entry.obj.GetType().GetMethod("Read").Invoke(entry.obj, [new EndianReader(backupStream, currentScene.endian), false, dir, entry]);
                                }
                            }
                        }
                    }

                    FindReferencesMenu(entry.name);
                }

                ImGui.PopFont();
                ImGui.EndPopup();
            }
            ImGui.PopStyleVar();
            ImGui.PopStyleVar();
        }

        var childrenDrawn = 0;
        PushTrace();
        if (Util.SceneTreeItem(dir, flags))
        {
            DirectoryContextMenu(ref iterId);
            unsafe
            {
                if (ImGui.BeginDragDropSource())
                {
                    var payload = dir.name.ToString();
                    var payloadBytes = Encoding.UTF8.GetBytes(payload);
                    fixed (byte* payloadPtr = payloadBytes)
                    {
                        ImGui.SetDragDropPayload("TreeEntryDir", (IntPtr)(payloadPtr), (uint)payloadBytes.Length);

                        ImGui.Text(payload);
                        ImGui.EndDragDropSource();
                    }
                }
            }

            if (ImGui.IsItemActivated() && !ImGui.IsItemToggledOpen())
                NavigateObject(dir.directory);
            var nodeId = 200;
            //ImGui.Indent();
            if (dir.directory is ObjectDir objDir && objDir.inlineSubDirs.Count > 0)
            {
                childrenDrawn++;
                var childLinePos = ImGui.GetCursorScreenPos();
                PushTrace();
                if (Util.IconTreeItem("ObjectDir", "Inline Subdirectories", ImGuiTreeNodeFlags.SpanFullWidth))
                {

                    for (int subDirIndex = 0; subDirIndex < objDir.inlineSubDirs.Count; subDirIndex++)
                    {
                        var subDir = objDir.inlineSubDirs[subDirIndex];
                        var subDirChildLinePos = ImGui.GetCursorScreenPos();
                        DirNode(subDir, ref subDirIndex, nodeId, false, dir, true);
                        DrawChildLine(true, subDirChildLinePos);
                        nodeId++;
                    }
                    ImGui.TreePop();
                    PopTrace();
                }
                else
                {
                    traceStack.Pop();
                }
                DrawChildLine(true, childLinePos);
            }

            nodeId = 1000; // Count of inlined dirs was affecting future node ids, just set it to 1000 and hope something doesn't have >1000 inlined subdirs

            for (int entryIndex = 0; entryIndex < dir.entries.Count; entryIndex++)
            {
                var entry = dir.entries[entryIndex];
                var matchesFilter = filterActive && entry.name.ToString().ToLower().Contains(filter.ToLower());
                nodeId++;
                if (matchesFilter)
                {
                    if (IsThemeDark())
                    {
                        ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(1f, 1f, 0f, 1f));
                    }
                    else
                    {
                        ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0f, 0f, 0.7f, 1f));
                    }

                }
                var childLinePos = ImGui.GetCursorScreenPos();

                if (entry.dir != null)
                {
                    //ImGui.Button("Test");
                    //ImGui.SameLine();

                    DirNode(entry.dir, ref entryIndex, nodeId, false, dir, false, entry, false);
                    DrawChildLine(true, childLinePos);
                    childrenDrawn++;
                }
                else
                {
                    if (filterActive && !matchesFilter)
                    {
                        continue;
                    }

                    if (entry.obj == null)
                    {
                        ImGui.PushStyleVar(ImGuiStyleVar.Alpha, 0.5f);
                    }

                    ImGuiTreeNodeFlags leafFlags = ImGuiTreeNodeFlags.Leaf | ImGuiTreeNodeFlags.SpanFullWidth;
                    if ((viewingObject != null && viewingObject == entry.obj) || selectedEntries.Contains(entry))
                    {
                        leafFlags |= ImGuiTreeNodeFlags.Selected;
                    }


                    Util.SceneTreeItem(entry, leafFlags);
                    DrawChildLine(false, childLinePos);
                    ItemContextMenu(entry, ref entryIndex);
                    childrenDrawn++;
                    if (ImGui.IsItemClicked(ImGuiMouseButton.Left))
                    {
                        bool ctrlPressed = ImGui.GetIO().KeyCtrl;

                        if (ctrlPressed)
                        {
                            // Toggle selection for this item
                            if (selectedEntries.Contains(entry))
                            {
                                selectedEntries.Remove(entry);
                            }
                            else
                            {
                                selectedEntries.Add(entry);
                            }

                            // Still navigate to the object if it's the only one selected
                            if (selectedEntries.Count == 1 && entry.obj != null)
                            {
                                NavigateObject(entry.obj);
                            }
                        }
                        else
                        {
                            // Clear selection if ctrl is not pressed
                            if (!selectedEntries.Contains(entry))
                            {
                                selectedEntries.Clear();
                            }

                            // Add this item to selection
                            if (entry.obj != null)
                            {
                                selectedEntries.Add(entry);
                                NavigateObject(entry.obj);
                            }
                        }
                    }

                    unsafe
                    {
                        if (ImGui.BeginDragDropSource())
                        {
                            var payload = entry.name.ToString();
                            var payloadBytes = Encoding.UTF8.GetBytes(payload);
                            fixed (byte* payloadPtr = payloadBytes)
                            {
                                ImGui.SetDragDropPayload("TreeEntryObject", (IntPtr)(payloadPtr),
                                    (uint)payloadBytes.Length);
                            }

                            ImGui.Text(payload);
                            ImGui.EndDragDropSource();
                        }
                    }

                    ImGui.TreePop();
                    if (entry.obj == null)
                    {
                        ImGui.PopStyleVar();
                    }
                }

                if (matchesFilter)
                {
                    ImGui.PopStyleColor();
                }
            }

            ImGui.TreePop();
            if (childrenDrawn > 0)
            {
                PopTrace();
            }
            //ImGui.Unindent();
        }
        else
        {
            DirectoryContextMenu(ref iterId);
            if (ImGui.IsItemClicked() && !ImGui.IsItemToggledOpen())
                NavigateObject(dir.directory);
        }

        ImGui.PopID();
        if (Settings.Editing.CompactSceneTree)
        {
            ImGui.PopStyleVar();
        }
    }

    // Add this method to handle keyboard shortcuts
    private static void HandleKeyboardShortcuts()
    {
        // Check for Delete key to delete selected entries
        if (ImGui.IsKeyPressed(ImGuiKey.Delete) && selectedEntries.Count > 0 && ImGui.IsWindowFocused())
        {
            DeleteSelectedEntries();
        }

        // Check for Ctrl+A to select all entries in current directory
        if (ImGui.IsKeyPressed(ImGuiKey.A) && ImGui.GetIO().KeyCtrl && currentScene != null)
        {
            SelectAllEntriesInCurrentDirectory();
        }
    }

    private static void DeleteSelectedEntries()
    {
        if (selectedEntries.Count == 0)
            return;

        // Group entries by their parent directory
        var entriesByDir = new Dictionary<DirectoryMeta, List<DirectoryMeta.Entry>>();

        foreach (var entry in selectedEntries)
        {
            // Find the parent directory for this entry
            DirectoryMeta parentDir = FindParentDirectory(entry);
            if (parentDir != null)
            {
                if (!entriesByDir.ContainsKey(parentDir))
                {
                    entriesByDir[parentDir] = new List<DirectoryMeta.Entry>();
                }
                entriesByDir[parentDir].Add(entry);
            }
        }

        // Delete entries from each directory
        foreach (var kvp in entriesByDir)
        {
            DirectoryMeta dir = kvp.Key;
            List<DirectoryMeta.Entry> entries = kvp.Value;

            foreach (var entry in entries)
            {
                if (viewingObject == entry.obj)
                {
                    viewingObject = null;
                }
                dir.entries.Remove(entry);
            }
        }

        // Clear selection after deletion
        selectedEntries.Clear();
    }

    private static DirectoryMeta FindParentDirectory(DirectoryMeta.Entry entry)
    {
        // This is a simplified approach - in a real implementation,
        // you would need to traverse the scene hierarchy to find the parent
        if (currentScene != null)
        {
            if (currentScene.dirMeta.entries.Contains(entry))
                return currentScene.dirMeta;

            // Search in subdirectories
            return FindParentDirectoryRecursive(currentScene.dirMeta, entry);
        }
        return null;
    }

    private static DirectoryMeta FindParentDirectoryRecursive(DirectoryMeta dir, DirectoryMeta.Entry entry)
    {
        if (dir.entries.Contains(entry))
            return dir;

        // Check in subdirectories if this is an ObjectDir
        if (dir.directory is ObjectDir objDir)
        {
            foreach (var subDir in objDir.inlineSubDirs)
            {
                var result = FindParentDirectoryRecursive(subDir, entry);
                if (result != null)
                    return result;
            }
        }

        // Check in directory entries
        foreach (var dirEntry in dir.entries)
        {
            if (dirEntry.dir != null)
            {
                var result = FindParentDirectoryRecursive(dirEntry.dir, entry);
                if (result != null)
                    return result;
            }
        }

        return null;
    }

    private static void SelectAllEntriesInCurrentDirectory()
    {
        selectedEntries.Clear();

        // Find the current directory being viewed
        DirectoryMeta currentDir = null;

        if (viewingObject is ObjectDir objDir)
        {
            // Find the DirectoryMeta that contains this ObjectDir
            currentDir = FindDirectoryMetaForObjectDir(objDir);
        }

        if (currentDir != null)
        {
            // Select all entries in this directory
            foreach (var entry in currentDir.entries)
            {
                if (entry.obj != null)
                {
                    selectedEntries.Add(entry);
                }
            }
        }
    }

    private static DirectoryMeta FindDirectoryMetaForObjectDir(ObjectDir objDir)
    {
        // Start with the root directory
        if (currentScene.dirMeta.directory == objDir)
            return currentScene.dirMeta;

        return FindDirectoryMetaForObjectDirRecursive(currentScene.dirMeta, objDir);
    }

    private static DirectoryMeta FindDirectoryMetaForObjectDirRecursive(DirectoryMeta dir, ObjectDir objDir)
    {
        // Check if this directory matches
        if (dir.directory == objDir)
            return dir;

        // Check in subdirectories if this is an ObjectDir
        if (dir.directory is ObjectDir currentObjDir)
        {
            foreach (var subDir in currentObjDir.inlineSubDirs)
            {
                var result = FindDirectoryMetaForObjectDirRecursive(subDir, objDir);
                if (result != null)
                    return result;
            }
        }

        // Check in directory entries
        foreach (var entry in dir.entries)
        {
            if (entry.dir != null)
            {
                var result = FindDirectoryMetaForObjectDirRecursive(entry.dir, objDir);
                if (result != null)
                    return result;
            }
        }

        return null;
    }
}