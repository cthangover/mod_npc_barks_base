namespace Cthangover.Mods.NpcBarks.ToolTests
{
    using System;
    using Cthangover.Core.Bootstrap;
    using Cthangover.Core.Mods;
    using Cthangover.Core.Scenes;
    using Cthangover.Core.Settings;
    using Cthangover.Core.UI;
    using Cthangover.Core.UI.Tool;
    using Cthangover.Core.UI.Tool.ScenarioEditor;
    using Cthangover.Core.Utils;
    using Cthangover.Mods.Dialog;
    using Cthangover.Mods.Rpg;
    using Cthangover.Mods.ScenarioStudio;
    using Cthangover.Tools.Services;
    using Godot;

    /// <summary>Drives the real Studio tab and editor, preserving fixture files even after failures and shutdown.</summary>
    public partial class BarkToolsTestDriver : Node
    {
        private const string Owner = "npc_barks_base__tools";
        private const string File = "scenarios/tools/alpha.scenario";
        private int step;
        private int passed;
        private int failed;
        private double wait;
        private double total;
        private double remainingA;
        private double remainingB;
        private double clock;
        private string alphaId;
        private string original;
        private string shots;
        private StudioPanel studio;
        private ScenarioEditorWindow editor;
        private CodeEdit source;
        private static IBackgroundScenarioTools Tools => BackgroundScenarios.Provider;
        public override void _Ready()
        {
            foreach (var arg in OS.GetCmdlineUserArgs()) if (arg.StartsWith("--shots=", StringComparison.Ordinal)) shots = arg.Substring(8);
            original = ScenarioFileService.ReadAllText(Owner, File);
        }
        public override void _ExitTree() { Restore(); }
        public override void _Process(double delta)
        {
            if (step < 0) return;
            wait += delta; total += delta;
            if (total > 70) { Check(false, "watchdog at step " + step); Finish(); return; }
            try { Advance(); }
            catch (Exception ex) { Check(false, ex.ToString()); Finish(); }
        }
        private void Advance()
        {
            switch (step)
            {
                case 0:
                    if (!Boot.IsReady || string.IsNullOrEmpty(Manager().CurrentSceneName) || RpgWorldHost.Current == null || GodotSceneService.IsTransitioning) return;
                    Manager().SwitchScene("npc_barks_tools"); Next(); return;
                case 1:
                    if (RpgWorldHost.Current?.Session?.Map.ID != "npc_barks_tools" || wait < 0.5 || GodotSceneService.IsTransitioning) return;
                    Check(Tools != null, "background tools provider is available");
                    ScenarioCondition.SetFlag("tools_enabled", "1"); Next(); return;
                case 2:
                    if (wait < 0.4) return;
                    Check(Run("tools/alpha") != null && Run("tools/beta") != null, "two executions started");
                    AppServices.Current.Ui.ToggleEntry("studio"); Next(); return;
                case 3:
                    if (wait < 0.4) return;
                    UiHost.Instance.TryGetInstance("studio", out var panel); studio = (StudioPanel)panel;
                    studio.ShowBackground("tools/alpha");
                    Check(studio.Tab == StudioTab.Background, "existing studio opens the Background tab");
                    alphaId = Run("tools/alpha").RunId;
                    studio.BackgroundPage.SelectExecution(alphaId);
                    Click(studio.BackgroundPage, "Pause");
                    remainingA = Run("tools/alpha").Remaining; remainingB = Run("tools/beta").Remaining;
                    Next(); return;
                case 4:
                    if (wait < 0.8) return;
                    Check(Math.Abs(Run("tools/alpha").Remaining - remainingA) < 0.02, "selected execution clock is paused");
                    Check(Run("tools/beta").Remaining < remainingB - 0.4, "another NPC continues while Studio is open");
                    Click(studio.BackgroundPage, "Clock");
                    Check(Math.Abs(Run("tools/alpha").Remaining - (remainingA - 1)) < 0.02, "+1 second affects only the selected execution");
                    var code = Find<CodeEdit>(studio.BackgroundPage);
                    var line = Line(Run("tools/alpha").Body, "flag tools_effect 1");
                    code.EmitSignal(CodeEdit.SignalName.BreakpointToggled, line - 1);
                    Check(Tools.HasBreakpoint("tools/alpha", line), "code gutter sets a scenario breakpoint");
                    Click(studio.BackgroundPage, "Step");
                    Click(studio.BackgroundPage, "Continue"); Next(); return;
                case 5:
                    if (wait < 0.5) return;
                    Check(Run("tools/alpha").Paused && !Flag("tools_effect"), "breakpoint holds before the effect");
                    Check(Run("tools/reply") != null, "completed speech triggered a separate NPC during debugging");
                    studio.BackgroundPage.SelectExecution(alphaId);
                    Click(studio.BackgroundPage, "Step");
                    Check(Flag("tools_effect"), "Step executes the held source command");
                    Tools.WorldPaused = true; clock = BarkDirector.Current.Clock; remainingB = Run("tools/beta").Remaining;
                    Next(); return;
                case 6:
                    if (wait < 0.5) return;
                    Check(Math.Abs(BarkDirector.Current.Clock - clock) < 0.001 && Math.Abs(Run("tools/beta").Remaining - remainingB) < 0.02,
                        "world pause freezes all simulation clocks while tool controls remain usable");
                    Tools.WorldPaused = false;
                    Click(studio.BackgroundPage, "OpenEditor"); Next(); return;
                case 7:
                    if (wait < 0.4) return;
                    editor = Find<ScenarioEditorWindow>(GetTree().Root);
                    Check(editor != null && editor.Visible, "Open in editor reuses the existing scenario editor");
                    source = Find<ScenarioEditTabs>(editor).CodeEditor;
                    remainingB = Run("tools/beta").Remaining;
                    source.GrabFocus(); Next(); return;
                case 8:
                    if (wait < 0.6) return;
                    Check(Run("tools/beta").Remaining < remainingB - 0.3, "focused scenario text editor does not freeze background simulation");
                    source.Text = original.Replace("end", "goto -> :missing");
                    Check(!editor.SaveActive(), "invalid edited source is rejected before writing");
                    Check(ScenarioFileService.ReadAllText(Owner, File) == original, "invalid save preserves the file");
                    source.Text = original.Replace("Первый: исходный текст", "Первый: изменённый текст").Replace("key=npc_barks/tools/alpha ", "key=npc_barks/tools/alpha_edited ");
                    Check(editor.SaveActive(), "valid source saves through the existing editor");
                    Check(Catalog("tools/alpha").Source.Contains("alpha_edited"), "save refreshes the live catalog without restart");
                    Check(Run("tools/alpha").RunId == alphaId && !Run("tools/alpha").Body.Contains("alpha_edited"), "running execution retains its old source snapshot");
                    editor.Hide();
                    studio.BackgroundPage.SelectExecution(alphaId); Click(studio.BackgroundPage, "Restart"); Next(); return;
                case 9:
                    if (wait < 0.3) return;
                    Check(Run("tools/alpha").RunId != alphaId && Run("tools/alpha").Body.Contains("alpha_edited"), "Restart uses the edited program in a new independent runtime");
                    alphaId = Run("tools/alpha").RunId;
                    studio.BackgroundPage.SelectExecution(alphaId); Click(studio.BackgroundPage, "Continue"); Next(); return;
                case 10:
                    if (wait < 0.2) return;
                    Check(Run("tools/alpha").Text == "Первый: изменённый текст", "edited localized speech is visible");
                    Capture("studio_background");
                    studio.GetNode<ScrollContainer>("Frame/Dock/Column/Body/Scroll").ScrollVertical = 460;
                    editor.Show();
                    source.Text = source.Text.Replace("set marker alpha", "set marker unsaved_buffer");
                    editor._UnhandledInput(new InputEventKey { Keycode = Key.F5, PhysicalKeycode = Key.F5, Pressed = true });
                    Next(); return;
                case 11:
                    if (wait < 0.4) return;
                    Check(!SceneManager.IsTestPlayActive && RpgInput.FindDialogBox()?.IsRunning != true, "F5 chooses ambient execution rather than the modal test-play path");
                    Check(!editor.Visible && Run("tools/alpha")?.Text == "Первый: изменённый текст", "F5 previews the source buffer on the map");
                    Check(Run("tools/alpha").Variables.TryGetValue("marker", out var marker) && marker == "unsaved_buffer", "F5 executes unsaved buffer changes");
                    Check(!ScenarioFileService.ReadAllText(Owner, File).Contains("unsaved_buffer"), "preview does not silently save the buffer");
                    alphaId = Run("tools/alpha").RunId;
                    studio.BackgroundPage.SelectExecution(alphaId); Click(studio.BackgroundPage, "Stop");
                    remainingB = Run("tools/beta").Remaining; Next(); return;
                case 12:
                    if (wait < 0.5) return;
                    Check(Run("tools/alpha") == null && Run("tools/beta").Remaining < remainingB, "Stop affects only the selected execution and prevents an automatic restart");
                    Check(Tools.Events.Count > 0, "studio can inspect the bounded event and condition trace");
                    Check(editor.Visible && source.Text.Contains("unsaved_buffer"), "ending the ambient preview restores the same editor and unsaved buffer");
                    editor._UnhandledInput(new InputEventKey { Keycode = Key.F5, PhysicalKeycode = Key.F5, Pressed = true });
                    Next(); return;
                case 13:
                    if (wait < 0.3) return;
                    Check(!editor.Visible && Run("tools/alpha") != null, "a second preview starts without recreating the editor");
                    Click(GetTree().Root, "ReturnToEditor"); Next(); return;
                case 14:
                    if (wait < 0.15) return;
                    Check(editor.Visible && Run("tools/alpha") == null, "preview Stop button returns to the editor and cancels only its scenario");
                    Capture("studio_code");
                    editor.Hide();
                    studio.Hide();
                    Check(!BarkDirector.Current.IsObserved && !Tools.WorldPaused, "closing tools releases observation and world-pause ownership");
                    Finish(); return;
            }
        }
        private static BackgroundRunInfo Run(string id)
        {
            foreach (var run in Tools.Executions()) if (run.ScenarioId == id) return run;
            return null;
        }
        private static BackgroundScenarioInfo Catalog(string id)
        {
            foreach (var source in Tools.Catalog()) if (source.Id == id) return source;
            return null;
        }
        private static T Find<T>(Node root) where T : Node
        {
            if (root is T match) return match;
            foreach (var child in root.GetChildren()) { var result = Find<T>(child); if (result != null) return result; }
            return null;
        }
        private static void Click(Node root, string name)
        {
            var button = root.FindChild(name, true, false) as Button;
            if (button == null) throw new InvalidOperationException("Button missing: " + name);
            button.EmitSignal(BaseButton.SignalName.Pressed);
        }
        private static int Line(string body, string command)
        {
            var rows = body.Split('\n');
            for (var i = 0; i < rows.Length; i++) if (rows[i].Trim() == command) return i + 1;
            throw new InvalidOperationException(command);
        }
        private static bool Flag(string key) => GameData.Instance.Runtime.Flags.TryGetValue(key, out var value) && value == "1";
        private SceneManager Manager() => GetTree().Root.GetNode<SceneManager>("SceneManager");
        private void Next() { step++; wait = 0; }
        private void Check(bool result, string message)
        {
            if (result) passed++; else failed++;
            GameLogger.Log("TEST", (result ? "[PASS] " : "[FAIL] ") + message, result ? LogLevel.Message : LogLevel.Error);
        }
        private void Capture(string name)
        {
            if (shots == null || DisplayServer.GetName() == "headless") return;
            System.IO.Directory.CreateDirectory(shots);
            using var image = GetViewport().GetTexture().GetImage();
            image.SavePng(System.IO.Path.Combine(shots, name + ".png"));
        }
        private void Restore()
        {
            if (original == null) return;
            ScenarioFileService.WriteAllText(Owner, File, original);
            BackgroundScenarios.Provider?.Reload(out _);
            original = null;
        }
        private void Finish()
        {
            step = -1; Restore();
            if (editor != null && IsInstanceValid(editor)) editor.QueueFree();
            GameLogger.Log("TEST", "[TEST_RESULT] name=npc_barks_tools passed=" + passed + " failed=" + failed);
            ApplicationExit.Quit(GetTree(), failed == 0 ? 0 : 1);
        }
    }
}
