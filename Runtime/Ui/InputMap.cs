using System;
using System.Collections.Generic;

namespace Demech2
{
    // The simulator's controls (demech2 MW2/src/inputmap.c: g_inputSinks, UpdateInputs, UpdateAxisFromKeys): bindings
    // drive named sinks, and each update leaves the sinks' values for the mech (g_localSteering) and the eyepoint. Axes
    // keep a position in [-0x10000, 0x10000] that their keys ramp (up and down) or their control sets, and output it
    // scaled to the sink's range. Values are the original's integers: 16.16 degrees for the torso and pilot sinks, the
    // throttle a plain number (1024 full).
    // Not the original's: the bindings and their controls are the game's (Host.Input: it binds them at FirstInputs and
    // polls the devices), and its game keys run on a binding's press in place of GAMEKEY.MAP's released key codes.
    // The original ran an update a rendered frame, so its key ramps followed the frame rate; here they follow the sim clock.
    public static class InputMap
    {
        enum ChannelKind { Axis, Button, Press }

        sealed class Channel
        {
            public string Name;
            public ChannelKind Kind;
            public int Rest, Min, Max, RampShift, OutputShift;
            public bool MenuOnly; // read even while gameplay input is off (the sim's menus)
            public bool Recenters; // an axis whose keys' rate is its position, so it recenters on release
            public string IncKey, DecKey, HoldKey, CenterKey; // an axis's keys: ramp up, ramp down, hold its output, recenter
            public string Drives; // a key's axis, which it ramps ("" for none)
            public int Value; // the output (an axis's scaled position, a button's byte)
        }

        // InputAxis.
        sealed class Axis
        {
            public Channel Output;
            public bool RateIsPosition; // a recentering axis: its keys' rate is its position
            public int OwnRate;
            public int Position;
            public int Range, MinFixed, OutputShift, Rest, RampShift;
            public Channel PlusHeld, MinusHeld, SetHeld, ResetHeld;
            public bool Driven;

            public int Rate
            {
                get => RateIsPosition ? Position : OwnRate;
                set { if (RateIsPosition) Position = value; else OwnRate = value; }
            }
        }

        // One bound input: a control path (the game's control) and the modifiers it needs. Inputs on the same control
        // are its siblings: the one needing the most of the held modifiers wins. The game polls it each rendered frame.
        public sealed class InputSource
        {
            public string Path;
            public int Mods;
            public object Control;
            public List<InputSource> Siblings;
            public bool Held, Previous;
            public bool Tapped; // Not the original's: pressed on a frame since the last sim step
            public bool FrameDown; // Not the original's: held on the last rendered frame
            public bool FrameTapped; // Not the original's: pressed on the last rendered frame
            public float FrameValue; // Not the original's: an axis control's value on the last rendered frame
        }

        sealed class AnalogBinding
        {
            public Axis Axis;
            public InputSource Input; // null for the axis's keys alone
            public int Sign = 1;
            public int LastValue;
            public bool MenuOnly;
            public bool HasChannel => Input != null;
        }

        sealed class DiscreteBinding
        {
            public Channel Output;
            public InputSource Input;
            public bool Edge, Previous, MenuOnly;
        }

        public sealed class GameKeyBinding
        {
            public int Action;
            public InputSource Input;
            public bool FrameHeld; // Not the original's: held on the last rendered frame (the game's per-frame keys)
        }

        // The channels, in table order (StreamingAssets/Tables/ControlChannels.json): id, kind, rest, range, ramp and scale
        // shifts, whether the menus read it, and its related keys. Read on first use and kept, so their values persist
        // across loads.
        static Channel[] Channels => s_channels ??= LoadChannels();
        static Channel[] s_channels;

        static Channel[] LoadChannels()
        {
            ChannelRow[] rows = SimTables.ControlChannels;
            var channels = new Channel[rows.Length];
            for (int i = 0; i < rows.Length; i++)
            {
                ChannelRow row = rows[i];
                channels[i] = new Channel
                {
                    Name = row.id, Kind = (ChannelKind)Enum.Parse(typeof(ChannelKind), row.kind), Rest = row.rest, Min = row.low,
                    Max = row.high, RampShift = row.rampShift, OutputShift = row.scaleShift, MenuOnly = row.menuOnly,
                    Recenters = row.recenters, IncKey = row.incKey, DecKey = row.decKey, HoldKey = row.holdKey,
                    CenterKey = row.centerKey, Drives = row.drives,
                };
            }

            return channels;
        }

        static readonly List<Axis> axes = new List<Axis>();
        static readonly List<AnalogBinding> analogBindings = new List<AnalogBinding>();
        static readonly List<DiscreteBinding> discreteBindings = new List<DiscreteBinding>();
        static readonly List<GameKeyBinding> gameKeyBindings = new List<GameKeyBinding>();
        static readonly List<InputSource> inputs = new List<InputSource>();
        static bool loaded;
        static int lastClock;

        // The bound inputs and game keys, for the game's polling; whether FirstInputs has run.
        public static List<InputSource> Inputs => inputs;
        public static List<GameKeyBinding> GameKeyBindings => gameKeyBindings;
        public static bool Loaded => loaded;

        // g_gameplayInputEnabled.
        public static bool GameplayInputEnabled = true;

        // Not the original's: the local mech's torso turns freely (Mech.FreeTorsoTwist), so the torso yaw is an angle
        // the controls spin (UpdateFreeTorsoPan) rather than a position within the axis's range.
        public static bool FreeTorsoPan;

        // The free torso's spin with a twist key held or the stick full over: 16.16 degrees a second.
        const int FreeSpinRate = 180 << 16;

        [UnityEngine.RuntimeInitializeOnLoadMethod(UnityEngine.RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            loaded = false;
            GameplayInputEnabled = true;
            FreeTorsoPan = false;
        }

        // A sink's value (an axis's output, a button's 0 or 1), 0 for an unknown sink.
        public static int Get(string p_name)
        {
            Channel sink = FindChannel(p_name);
            return sink != null ? sink.Value : 0;
        }

        // Not the original's: an axis sink's value as its controls stand on this rendered frame, without updating; the
        // sim's value while its keys drive it or no control is bound. For render-side prediction.
        public static int Peek(string p_name)
        {
            Channel sink = FindChannel(p_name);
            if (sink == null) return 0;
            int output = sink.Value;
            if (!loaded || !GameplayInputEnabled || (FreeTorsoPan && sink.Name == "torso.yaw")) return output;
            foreach (AnalogBinding binding in analogBindings)
            {
                Axis axis = binding.Axis;
                if (axis.Output != sink || !binding.HasChannel || binding.MenuOnly) continue;
                if (axis.Driven) return sink.Value;
                long position = Math.Max(-0x10000, Math.Min(0x10000, Host.Input.AxisValue(binding.Input) * binding.Sign)) + 0x10000;
                position = axis.Range * position / 2 + axis.MinFixed;
                output = (int)(position >> axis.OutputShift);
            }

            return output;
        }

        // Whether there is a sink of that name.
        public static bool Has(string p_name) => FindChannel(p_name) != null;

        // Writes a sink's value, as the game writes g_localSteering's (the throttle keys, the torso's limits).
        public static void Set(string p_name, int p_value)
        {
            Channel sink = FindChannel(p_name);
            if (sink != null) sink.Value = p_value;
        }

        // FirstInputs: the bindings of every action (the game's: AddInput, BindAxis, BindButton), then the game keys' state.
        public static void Load()
        {
            axes.Clear();
            analogBindings.Clear();
            discreteBindings.Clear();
            gameKeyBindings.Clear();
            inputs.Clear();
            // Not the original's: an axis starts at its rest, so an unbound one (or the frames before the first Tick)
            // reads as centred, not 0 (a zoom of 0 is a 180-degree view).
            foreach (Channel sink in Channels) sink.Value = sink.Kind == ChannelKind.Axis ? (sink.Rest << 16) >> sink.OutputShift : 0;
            Host.Input.LoadBindings();
            GameKeys.Load();
            loaded = true;
            lastClock = Clock.g_currentClock;
        }

        public static InputSource AddInput(string p_path, int p_mods)
        {
            var input = new InputSource { Path = p_path, Mods = p_mods };
            foreach (InputSource other in inputs)
            {
                if (other.Path != p_path) continue;
                input.Siblings = other.Siblings;
                break;
            }

            input.Siblings = input.Siblings ?? new List<InputSource>();
            input.Siblings.Add(input);
            inputs.Add(input);
            return input;
        }

        // An axis action's control drives its axis and the axes following it ('-' inverted).
        public static void BindAxis(string[] p_channels, InputSource p_input, bool p_invert)
        {
            foreach (string name in p_channels)
            {
                bool inverted = name.StartsWith("-", StringComparison.Ordinal);
                Channel sink = FindChannel(inverted ? name.Substring(1) : name);
                if (sink == null || sink.Kind != ChannelKind.Axis) continue;
                analogBindings.Add(new AnalogBinding
                {
                    Axis = GetAxis(sink),
                    Input = p_input,
                    Sign = inverted != p_invert ? -1 : 1,
                    MenuOnly = sink.MenuOnly,
                });
            }
        }

        // A button action's input holds its sinks (a press sink on the press alone) and runs its game key.
        public static void BindButton(string[] p_channels, int p_gameKey, InputSource p_input)
        {
            foreach (string name in p_channels)
            {
                Channel sink = FindChannel(name);
                if (sink == null) continue;
                discreteBindings.Add(new DiscreteBinding
                {
                    Output = sink,
                    Input = p_input,
                    Edge = sink.Kind == ChannelKind.Press,
                    MenuOnly = sink.MenuOnly,
                });

                // A ramp key also binds the axis it drives, so it ramps it.
                Channel axis = FindChannel(sink.Drives);
                if (axis != null) AddKeysBinding(axis);
            }

            if (p_gameKey > 0) gameKeyBindings.Add(new GameKeyBinding { Action = p_gameKey, Input = p_input });
        }

        static void AddKeysBinding(Channel p_sink)
        {
            foreach (AnalogBinding existing in analogBindings)
            {
                if (existing.Axis.Output == p_sink) return;
            }

            analogBindings.Add(new AnalogBinding
            {
                Axis = GetAxis(p_sink),
                MenuOnly = p_sink.MenuOnly,
            });
        }

        static Channel FindChannel(string p_name)
        {
            if (string.IsNullOrEmpty(p_name)) return null;
            foreach (Channel sink in Channels)
            {
                if (string.Equals(sink.Name, p_name, StringComparison.OrdinalIgnoreCase)) return sink;
            }

            return null;
        }

        // GetOrCreateInputAxis.
        static Axis GetAxis(Channel p_sink)
        {
            foreach (Axis existing in axes)
            {
                if (existing.Output == p_sink) return existing;
            }

            var axis = new Axis
            {
                Output = p_sink,
                RateIsPosition = p_sink.Recenters,
                Range = p_sink.Max - p_sink.Min,
                MinFixed = p_sink.Min << 16,
                OutputShift = p_sink.OutputShift,
                RampShift = p_sink.RampShift,
            };
            axis.Rest = ((p_sink.Rest << 16) - axis.MinFixed << 1) / axis.Range - 0x10000;
            axis.Position = axis.Rest;
            axis.PlusHeld = FindChannel(p_sink.IncKey);
            axis.MinusHeld = FindChannel(p_sink.DecKey);
            axis.SetHeld = FindChannel(p_sink.HoldKey);
            axis.ResetHeld = FindChannel(p_sink.CenterKey);
            axes.Add(axis);
            return axis;
        }

        static bool Held(Channel p_sink) => p_sink != null && p_sink.Value != 0;

        // UpdateAxisFromKeys.
        static bool UpdateAxisFromKeys(AnalogBinding p_binding, int p_deltaTime)
        {
            Axis axis = p_binding.Axis;
            bool driven = axis.Driven;
            if (Held(axis.ResetHeld))
            {
                if (p_binding.HasChannel) Host.Input.CenterAxis(p_binding.Input);
                axis.Rate = 0;
                axis.Position = axis.Rest;
                driven = true;
            }
            else if (Held(axis.SetHeld))
            {
                axis.Rate = 0;
                axis.Position = PositionOf(axis);
                driven = true;
            }

            if (!axis.Driven)
            {
                if (Held(axis.PlusHeld))
                {
                    if (axis.Rate < 0) axis.Rate = 0;
                    axis.Rate = Math.Min(axis.Rate + (p_deltaTime * 3 << axis.RampShift), 0x8000);
                    axis.Position = Math.Min(axis.Position + axis.Rate, 0x10000);
                    driven = true;
                }

                if (Held(axis.MinusHeld))
                {
                    if (axis.Rate > 0) axis.Rate = 0;
                    axis.Rate = Math.Max(axis.Rate - (p_deltaTime * 3 << axis.RampShift), -0x8000);
                    axis.Position = Math.Max(axis.Position + axis.Rate, -0x10000);
                    driven = true;
                }

                if (!driven) axis.Rate = 0;
            }

            axis.Driven = driven;
            return driven;
        }

        // The position the axis's output stands for (its hold key): ((output - min) * 2 / range - 65536), truncated, in
        // integers (the original's doubles; Range 0 the doubles' infinity, the x64 conversion's 0x80000000).
        static int PositionOf(Axis p_axis)
        {
            if (p_axis.Range == 0) return int.MinValue;
            long position = (((long)p_axis.Output.Value << p_axis.OutputShift) - p_axis.MinFixed) * 2;
            return (int)((position - 65536L * p_axis.Range) / p_axis.Range);
        }

        // Runs UpdateInputs with the clock ticks since the last; called once per sim step, before the mech.
        public static void Tick()
        {
            if (!loaded) return;
            // Each sim step consumes what the game's frames captured; the first of a frame's catch-up steps takes the edges.
            int clock = Clock.g_currentClock;
            int delta = clock - lastClock;
            lastClock = clock;
            UpdateInputs(delta);
        }

        // UpdateInputs, then the game keys pressed.
        static void UpdateInputs(int p_deltaTime)
        {
            foreach (AnalogBinding binding in analogBindings)
            {
                Axis axis = binding.Axis;
                if (Held(axis.ResetHeld))
                {
                    if (binding.HasChannel) Host.Input.CenterAxis(binding.Input);
                    axis.Position = axis.Rest;
                    axis.Rate = 0;
                    binding.LastValue = 0;
                }
                else if (Held(axis.SetHeld))
                {
                    axis.Rate = 0;
                    axis.Position = PositionOf(axis);
                    // Not the original's: the mouse moved to the set position, so turning back past a limit answers at once.
                    if (binding.HasChannel) Host.Input.MoveAxis(binding.Input, axis.Position * binding.Sign);
                }

                axis.Driven = false;
            }

            foreach (Channel sink in Channels)
            {
                if (sink.Kind != ChannelKind.Axis) sink.Value = 0;
            }

            foreach (InputSource input in inputs)
            {
                input.Previous = input.Held;
                input.Held = Host.Input.Pressed(input);
                input.Tapped = false;
            }

            foreach (DiscreteBinding binding in discreteBindings)
            {
                if (!GameplayInputEnabled && !binding.MenuOnly) continue;
                bool pressed = binding.Input.Held;
                if (!binding.Edge || !binding.Previous) binding.Output.Value |= pressed ? 1 : 0;
                binding.Previous = pressed;
            }

            bool freePan = FreeTorsoPan && GameplayInputEnabled;
            foreach (AnalogBinding binding in analogBindings)
            {
                if (!GameplayInputEnabled && !binding.MenuOnly) continue;
                Axis axis = binding.Axis;
                if (freePan && axis.Output.Name == "torso.yaw") continue;
                if (UpdateAxisFromKeys(binding, p_deltaTime)) { }
                else if (binding.HasChannel)
                {
                    int value = Host.Input.AxisValue(binding.Input) * binding.Sign;
                    // 1.1 compares the rate's value with the position: always equal for a recentering axis.
                    if (binding.LastValue != value) axis.Position = value;
                    else if (axis.Rate == axis.Position) axis.Position = value;
                    binding.LastValue = value;
                }

                long position = Math.Max(-0x10000, Math.Min(0x10000, axis.Position)) + 0x10000;
                position = axis.Range * position / 2 + axis.MinFixed;
                axis.Output.Value = (int)(position >> axis.OutputShift);
            }

            if (freePan) UpdateFreeTorsoPan(p_deltaTime);
            if (!GameplayInputEnabled || Host.Input.KeysPerFrame || Host.Input.DeadInMatch) return;
            foreach (GameKeyBinding binding in gameKeyBindings)
            {
                if (binding.Input.Held && !binding.Input.Previous && !Host.Input.KeyTaken(binding.Input)) GameKeys.RunGameKey(binding.Action);
            }
        }

        // Not the original's: the free torso's angle (FreeTorsoPan), within half a turn. The twist keys and a stick spin it
        // (FreeSpinRate at full); the mouse turns it as far as it moved (the axis's span over the window's width) and is
        // put back in the middle, so it never runs out of room. The center key faces it forward.
        static void UpdateFreeTorsoPan(int p_deltaTime)
        {
            Channel sink = FindChannel("torso.yaw");
            Axis axis = GetAxis(sink);
            if (Held(axis.ResetHeld))
            {
                Host.Input.CenterMouseX();
                sink.Value = 0;
                return;
            }

            long delta = 0;
            if (Held(axis.PlusHeld)) delta += (long)FreeSpinRate * p_deltaTime / Clock.TicksPerSecond;
            if (Held(axis.MinusHeld)) delta -= (long)FreeSpinRate * p_deltaTime / Clock.TicksPerSecond;
            foreach (AnalogBinding binding in analogBindings)
            {
                if (binding.Axis != axis || !binding.HasChannel) continue;
                if (Host.Input.MouseTurn(binding.Input, axis.Range, binding.Sign, out long turn)) delta += turn;
                else delta += (long)Host.Input.AxisValue(binding.Input) * binding.Sign * FreeSpinRate / 0x10000 * p_deltaTime / Clock.TicksPerSecond;
            }

            long angle = (sink.Value + delta) % 0x1680000;
            if (angle > 0xb40000) angle -= 0x1680000;
            else if (angle <= -0xb40000) angle += 0x1680000;
            sink.Value = (int)angle;
        }
    }
}
