namespace MangoThiefAI
{
    public sealed class ThiefMoveState : ThiefState
    {
        public override ThiefStateId Id => ThiefStateId.MoveToWaypoint;
        public ThiefMoveState(ThiefAI ai) : base(ai) { }

        public override void Tick(float deltaTime)
        {
            if (AI.Motor.Status == ThiefMoveStatus.Failed) AI.ReportMoveFailure();
            if (AI.Motor.Status != ThiefMoveStatus.Moving) AI.ReturnToIdle();
        }

        public override void Exit() => AI.Motor.Stop();
    }
}
