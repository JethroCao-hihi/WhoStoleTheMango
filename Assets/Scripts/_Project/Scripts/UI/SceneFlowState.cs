namespace MangoHeist.UI
{
    public enum GameSessionMode
    {
        None,
        Online,
        Offline
    }

    /// <summary>Local navigation state only; it does not start UGS or a network session.</summary>
    public static class SceneFlowState
    {
        public static GameSessionMode Mode { get; private set; }

        public static void EnterOnline() => Mode = GameSessionMode.Online;
        public static void EnterOffline() => Mode = GameSessionMode.Offline;
        public static void Clear() => Mode = GameSessionMode.None;
    }
}
