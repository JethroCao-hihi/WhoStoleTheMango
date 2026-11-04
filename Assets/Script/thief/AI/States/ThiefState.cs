namespace MangoThiefAI
{
    public enum ThiefStateId
    {
        Idle = 0, MoveToWaypoint = 1, SearchMango = 2, MoveToMango = 3,
        StealMango = 4, Hide = 5, Flee = 6, ClimbTree = 7, TreeHide = 8,
        ReturnToSteal = 9, ClimbDown = 10
    }

    public abstract class ThiefState
    {
        protected readonly ThiefAI AI;
        public abstract ThiefStateId Id { get; }
        protected ThiefState(ThiefAI ai) => AI = ai;
        public virtual void Enter() { }
        public abstract void Tick(float deltaTime);
        public virtual void Exit() { }
    }
}
