namespace Cthangover.Mods.NpcBarks.Prototype
{
    using System;
    using System.Collections.Generic;
    using Cthangover.Core.Bootstrap;
    using Cthangover.Core.Events;
    using Cthangover.Core.Localization;
    using Cthangover.Mods.Dialog;
    using Cthangover.Core.Scenes;
    using Cthangover.Core.Settings;
    using Cthangover.Core.Utils;
    using Cthangover.Mods.Rpg;
    using Godot;

    /// <summary>Exercises concurrent world-hosted scenarios, cue replies, patched departure, walking and modal handoff.</summary>
    public partial class BarkTestDriver : Node
    {
        private int step;
        private double total;
        private double wait;
        private int passed;
        private int failed;
        private int peak;
        private int answers;
        private bool duplicated;
        private string shots;
        public override void _Ready()
        {
            foreach (var arg in OS.GetCmdlineUserArgs()) if (arg.StartsWith("--shots=", StringComparison.Ordinal)) shots = arg.Substring(8);
            ModEventBus.Subscribe<BarkSpeechEvent>(OnSpeech);
        }
        public override void _ExitTree() { ModEventBus.Unsubscribe<BarkSpeechEvent>(OnSpeech); }
        public override void _Process(double delta)
        {
            if (step < 0) return;
            total += delta; wait += delta;
            if (total > 50) { Check(false, "watchdog at step " + step); Finish(); return; }
            peak = Math.Max(peak, BarkDirector.Current?.Runs.Count ?? 0);
            try { Advance(); }
            catch (Exception ex) { Check(false, ex.ToString()); Finish(); }
        }
        private void Advance()
        {
            var host = RpgWorldHost.Current;
            switch (step)
            {
                case 0:
                    if (!Boot.IsReady || GodotSceneService.IsTransitioning || string.IsNullOrEmpty(Manager().CurrentSceneName)
                        || RpgWorldHost.Current == null || RpgInput.FindDialogBox() == null) return;
                    Manager().SwitchScene("npc_barks_test"); Next(); return;
                case 1:
                    if (host?.Session?.Map.ID != "npc_barks_test" || GodotSceneService.IsTransitioning || wait < 0.5) return;
                    Check(RpgInput.FindDialogBox()?.IsRunning != true, "ambient definitions do not occupy DialogBox");
                    ScenarioCondition.SetFlag("bark_test_enabled", "1"); Next(); return;
                case 2:
                    if (wait < 0.4) return;
                    Check(BarkDirector.Current.Runs.Count >= 2, "two scenarios run concurrently");
                    Check(host.Session.Find("npc_barks_test/a").Actor.Facing == RpgDirection.Left, "speaking NPC looks toward the player");
                    LocaleLoader.LoadLocale("en");
                    Check(FindRun("npc_barks/initiator")?.Text == "First NPC asks a question", "active speech resolves the changed locale");
                    LocaleLoader.LoadLocale("ru");
                    Press(Key.S, true); Next(); return;
                case 3:
                    if (wait < 0.28) return;
                    Press(Key.S, false);
                    Check(host.Session.Player.Cell.Y > 4, "real movement is not blocked by ambient speech");
                    Next(); return;
                case 4:
                    if (wait < 1.1) return;
                    Check(Flag("bark_test_replied"), "finished question triggered the second NPC");
                    Check(peak >= 3, "response and independent conversation overlap");
                    Capture("concurrent");
                    host.PlacePlayer(12, 9, RpgDirection.Left); Next(); return;
                case 5:
                    if (wait < 1.6) return;
                    Check(Flag("bark_test_left"), "distance change selected departure branch");
                    Check(Flag("bark_test_patch"), "addressed scenario patch executed");
                    Check(Flag("bark_test_independent"), "independent NPC completed its own queue");
                    Check(answers == 1, "one utterance causes exactly one response");
                    Check(!host.Session.Find("npc_barks_test/a").RouteHeld, "completion releases actor ownership");
                    BarkMemory.Data.Cooldowns["npc_barks/initiator"] = 0;
                    host.PlacePlayer(3, 4, RpgDirection.Right); Next(); return;
                case 6:
                    if (wait < 0.3) return;
                    Check(host.Session.Find("npc_barks_test/a").RouteHeld, "new attempt owns the NPC");
                    var pause = new DialogQueue(); pause.Text("Pause for verification"); pause.End();
                    RpgInput.FindDialogBox().SetDialogQueueAndRun(pause, null, 0, null);
                    Next(); return;
                case 7:
                    if (wait < 1.3) return;
                    Check(FindRun("npc_barks/initiator")?.Text != null, "modal dialog pauses the ambient reading clock");
                    RpgInput.FindDialogBox().Abort(); Next(); return;
                case 8:
                    if (wait < 9.3) return;
                    Check(Flag("bark_test_stayed"), "condition wait takes else when its timeout expires");
                    BarkMemory.Data.Cooldowns["npc_barks/initiator"] = 0;
                    Next(); return;
                case 9:
                    if (wait < 0.3) return;
                    GameLogger.Log("TEST", "E input: cell=" + host.Session.Player.Cell + " facing=" + host.Session.Player.Facing
                        + " moving=" + host.Session.Player.IsMoving + " target=" + host.Session.InteractionTarget()?.Id
                        + " blocked=" + RpgInput.IsWalkBlocked(host) + " focus=" + GetViewport().GuiGetFocusOwner()?.Name);
                    Press(Key.E, true); Next(); return;
                case 10:
                    if (wait < 0.25) return;
                    Check(RpgInput.FindDialogBox()?.IsRunning == true, "E starts the ordinary modal interaction");
                    Check(!host.Session.Find("npc_barks_test/a").RouteHeld, "interaction interrupts ambient ownership");
                    Press(Key.E, false);
                    RpgInput.FindDialogBox()?.Abort();
                    Manager().SwitchScene("npc_barks_exit"); Next(); return;
                case 11:
                    if (wait < 0.5 || GodotSceneService.IsTransitioning) return;
                    Check(BarkDirector.Current.Runs.Count == 0, "scene exit drops old runs and events");
                    Manager().SwitchScene("jrp_gates"); Next(); return;
                case 12:
                    if (host?.Session?.Map.ID != "jrp_gates" || GodotSceneService.IsTransitioning || wait < 0.3) return;
                    var introduction = RpgInput.FindDialogBox();
                    if (introduction?.IsRunning == true)
                    {
                        if (!introduction.IsWaitingAnswer) introduction.NextAction();
                        return;
                    }
                    host.PlacePlayer(16, 13, RpgDirection.Right); Next(); return;
                case 13:
                    if (wait < 3.6) return;
                    Check(FindRun("jrp/ambient/jrp_barks_reply") != null, "the JRP company responds on its actual game map");
                    Capture("jrp_company"); Finish(); return;
            }
        }
        private static SceneManager Manager() => ((SceneTree)Engine.GetMainLoop()).Root.GetNode<SceneManager>("SceneManager");
        private static bool Flag(string key) => GameData.Instance.Runtime.Flags.TryGetValue(key, out var value) && value == "1";
        private static void Press(Key key, bool pressed) => Input.ParseInputEvent(new InputEventKey { PhysicalKeycode = key, Keycode = key, Pressed = pressed });
        private void Next() { step++; wait = 0; }
        private void OnSpeech(BarkSpeechEvent speech)
        {
            if (speech.Cue == "test_answer" && speech.Phase == "speech.finished") answers++;
            if (!duplicated && speech.Cue == "test_question" && speech.Phase == "speech.finished")
            {
                duplicated = true;
                ModEventBus.Publish(speech);
            }
        }
        private static BarkRun FindRun(string id)
        {
            foreach (var run in BarkDirector.Current.Runs) if (!run.Done && run.Definition.Id == id) return run;
            return null;
        }
        private void Check(bool result, string text)
        {
            if (result) passed++; else failed++;
            GameLogger.Log("TEST", (result ? "[PASS] " : "[FAIL] ") + text, result ? LogLevel.Message : LogLevel.Error);
        }
        private void Capture(string name)
        {
            if (string.IsNullOrEmpty(shots) || DisplayServer.GetName() == "headless") return;
            System.IO.Directory.CreateDirectory(shots);
            Cthangover.Mods.Interface.View.ViewportCapture.SavePng(GetViewport(), System.IO.Path.Combine(shots, name + ".png"));
        }
        private void Finish()
        {
            step = -1; Press(Key.S, false); Press(Key.E, false);
            GameLogger.Log("TEST", "[TEST_RESULT] name=npc_barks passed=" + passed + " failed=" + failed);
            ApplicationExit.Quit(GetTree(), failed == 0 ? 0 : 1);
        }
    }
}
