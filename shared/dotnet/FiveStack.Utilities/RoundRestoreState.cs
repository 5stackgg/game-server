namespace FiveStack.Utilities
{
    public readonly record struct OwedLoadTimeout(bool Expired, int? HoldRound = null);

    // A restore vote leaves the round running, because mp_pause_match waits for the
    // next freezetime, so its kills are real. A backup load outlasts what the api
    // clears when it restores a round, so anything reported then would stay in the
    // stats.
    //
    // A server that came back behind the backend is held on the round it can
    // restore until every player is back to vote on it. Meanwhile nothing is
    // played, published or written over the backups being restored. A passed vote
    // ends the hold before the load has started, and the api replies nothing to
    // the command, so the load is owed until it starts: resuming before then would
    // write round 1 over the round 1 the backend already has.
    public class RoundRestoreState
    {
        // Long, because a slow answer is not a lost one: the event is resent until
        // the api has handled it, so a panel deploy only delays it. Going back to
        // the hold early costs a second vote. Waiting costs a few paused minutes.
        public const int OwedSeconds = 300;

        // A hold on this has rounds recorded ahead of it and no backup of any of
        // them. There is nothing to vote on, and playing would go over them, so it
        // stays held until Restart Map.
        public const int NothingToLoad = 0;

        private bool _voting;
        private bool _recoveryVote;
        private int _owing;
        private int? _owedRound;
        private Guid? _map;
        private Guid? _heldMap;
        private int? _loadOwed;

        public int? HeldRound { get; private set; }

        public int? OwedRound => _owedRound;

        public bool LoadingBackup { get; private set; }

        public bool VotingOrLoading => _voting || LoadingBackup;

        public bool InProgress => VotingOrLoading || _owedRound != null;

        public bool Resetting => InProgress || HeldRound != null;

        public bool HoldingRounds => HeldRound != null || _recoveryVote || _owedRound != null;

        // Taking a hold to vote on it while something else is running loses it:
        // the vote is refused, and nothing puts the hold back.
        public bool CanOfferVote => HeldRound > NothingToLoad && !InProgress;

        public bool NeedsRestartMap => HeldRound == NothingToLoad;

        public void Hold(int round, Guid? map = null)
        {
            HeldRound = round;
            _heldMap = map;
        }

        // Says whether anything went, so the caller can put backups right.
        public bool LeaveMap(Guid? map)
        {
            bool left = false;

            if ((_voting || _owedRound != null) && _map != map)
            {
                End();
                left = true;
            }

            if (HeldRound != null && _heldMap != map)
            {
                HeldRound = null;
                left = true;
            }

            return left;
        }

        public int? ReleaseHold()
        {
            int? held = HeldRound;
            HeldRound = null;

            return held;
        }

        // A result waiting for the next round start is from the round this undoes.
        public event Action? LoadStarted;

        public void StartVote(bool recovering = false, Guid? map = null)
        {
            End();
            _voting = true;
            _recoveryVote = recovering;
            _map = map;
        }

        public int StartOwing(int round, Guid? map)
        {
            End();
            _owedRound = round;
            _map = map;

            return ++_owing;
        }

        // heldRound is a hold this load took over, which a failed load puts back.
        public void StartLoad(int? heldRound = null)
        {
            _loadOwed = _owedRound ?? heldRound;
            _owedRound = null;
            _voting = false;
            _recoveryVote = false;
            LoadingBackup = true;
            LoadStarted?.Invoke();
        }

        // The round to hold again, if a load was owed.
        public int? FailedLoad()
        {
            int? owed = _owedRound ?? _loadOwed;

            End();

            return owed;
        }

        public OwedLoadTimeout TimedOut(int owing, Guid? map)
        {
            if (owing != _owing || _owedRound == null)
            {
                return default;
            }

            int round = _owedRound.Value;
            bool sameMap = _map == map;

            End();

            return new OwedLoadTimeout(true, sameMap ? round : null);
        }

        public void End()
        {
            _voting = false;
            _recoveryVote = false;
            _owedRound = null;
            _map = null;
            _loadOwed = null;
            LoadingBackup = false;
        }

        public void Abandon()
        {
            End();
            HeldRound = null;
        }
    }
}
