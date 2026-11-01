namespace Demech2
{
    // A player's controls (playersteering.h): named as INPUT.MAP's sinks. The local player's read and write the game's
    // input map; the others' the AI sets. The mech's update clears a button once it has acted on it.
    public interface IPlayerSteering
    {
        int this[string p_name] { get; set; }
        bool Pressed(string p_name);
    }

    // What the sim tells the game's input.
    public interface IInputHost
    {
        // Not in the original: the local mech's torso turns freely (a human-piloted Tarantula), the controls spinning it.
        bool FreeTorsoPan { set; }

        // The local player's steering (g_localSteering): a sink's value (0 without one), and set (made if need be).
        int LocalSteering(string p_name);
        void SetLocalSteering(string p_name, int p_value);

        // Not the original's: a movie plays (Lockstep.Movies), and takes only the views' keys (true: p_action isn't one).
        bool Movie { get; }
        bool MovieBlocksKey(int p_action);

        // The mission's end: ended, quit (EXIT_SIM), the regenerate offered and asked for, the mission timer stopped.
        bool MissionEnded { get; }
        void QuitMission();
        bool RegenerateOffered { get; }
        void RequestRespawn();
        bool TimerStopped { get; }

        // Not the original's: the dev console takes every key.
        bool ConsoleBlocking { get; }

        // CRACK_MODE's time compression, and the cheat that enables its key.
        bool TimeCompressionKey { get; }
        bool TimeCompression { get; set; }

        // Titanium's SquadRollCall for the STARMATE_* keys; the targets dropped (the reset targeting key).
        void SquadRollCall(int p_which);
        void DropTargets();

        // FirstInputs' bindings: the game's actions bound (InputMap.AddInput, BindAxis, BindButton), their controls found.
        void LoadBindings();

        // An input held this step (with the modifiers it needs); an axis input's value in [-0x10000, 0x10000]; its device
        // centred (MouseCenterAxis), moved to read p_value; the mouse's x centred.
        bool Pressed(InputMap.InputSource p_input);
        int AxisValue(InputMap.InputSource p_input);
        void CenterAxis(InputMap.InputSource p_input);
        void MoveAxis(InputMap.InputSource p_input, int p_value);
        void CenterMouseX();

        // Not the original's: the free torso's turn by the mouse's x (the axis's span p_range over the window), the mouse
        // put back in the middle; false for another input.
        bool MouseTurn(InputMap.InputSource p_input, int p_range, int p_sign, out long p_turn);

        // Not the original's: the game keys run every rendered frame (a lockstep capturing this machine's input); the
        // local mech lost in a MekNet match (only its own keys run); a menu took the input's key.
        bool KeysPerFrame { get; }
        bool DeadInMatch { get; }
        bool KeyTaken(InputMap.InputSource p_input);

        // The menu keys: the frame's keyboard (false without one), and the menu key pressed (0 none; the navigation keys
        // too with p_navigates), taken from the game keys.
        bool BeginMenuKeys();
        int MenuKeyPressed(bool p_navigates);
    }
}
