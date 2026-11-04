namespace MangoThiefAI
{
    public sealed class ThiefIdleState : ThiefState
    {
        private float elapsed;
        public override ThiefStateId Id => ThiefStateId.Idle;
        public ThiefIdleState(ThiefAI ai) : base(ai) { }

        public override void Enter()
        {
            elapsed = 0f;
            AI.Motor.Stop();
        }

        public override void Tick(float deltaTime)
        {
            elapsed += deltaTime;
            if (elapsed < AI.IdleDuration) return;
            // One attempt per idle interval, including missing or unreachable waypoints.
            elapsed = 0f;
            AI.DecideMission();
        }
    }
}
