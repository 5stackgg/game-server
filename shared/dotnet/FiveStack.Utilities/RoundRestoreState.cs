namespace FiveStack.Utilities
{
    // A restore vote leaves the round running, because mp_pause_match waits for the
    // next freezetime, so its kills are real. A backup load outlasts what the api
    // clears when it restores a round, so anything reported then would stay in the
    // stats.
    public class RoundRestoreState
    {
        public bool InProgress { get; private set; }

        public bool LoadingBackup { get; private set; }

        public void StartVote()
        {
            InProgress = true;
        }

        public void StartLoad()
        {
            InProgress = true;
            LoadingBackup = true;
        }

        public void End()
        {
            InProgress = false;
            LoadingBackup = false;
        }
    }
}
