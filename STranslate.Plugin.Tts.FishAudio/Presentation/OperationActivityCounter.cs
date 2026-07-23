using System.Runtime.ExceptionServices;

namespace STranslate.Plugin.Tts.FishAudio.Presentation;

internal sealed class OperationActivityCounter
{
    private readonly object _gate;
    private readonly Action<bool> _setIsActive;
    private int _count;
    private bool _desiredIsActive;
    private long _stateVersion;
    private long _publishedStateVersion;
    private bool _isPublishing;

    internal OperationActivityCounter(object gate, Action<bool>? setIsActive = null)
    {
        _gate = gate;
        _setIsActive = setIsActive ?? (_ => { });
    }

    internal bool IsActive
    {
        get
        {
            lock (_gate)
                return _count > 0;
        }
    }

    internal bool Begin()
    {
        bool shouldPublish;
        bool transitioned;
        lock (_gate)
        {
            if (_count == int.MaxValue)
                throw new InvalidOperationException("Operation activity count exceeded its supported range.");

            _count++;
            transitioned = _count == 1;
            if (transitioned)
                SetDesiredState(true);
            shouldPublish = TryStartPublication();
        }

        if (shouldPublish)
            PublishObservableState();

        return transitioned;
    }

    internal bool End()
    {
        bool shouldPublish;
        bool transitioned;
        lock (_gate)
        {
            if (_count == 0)
            {
                transitioned = false;
            }
            else
            {
                _count--;
                transitioned = _count == 0;
                if (transitioned)
                    SetDesiredState(false);
            }

            shouldPublish = TryStartPublication();
        }

        if (shouldPublish)
            PublishObservableState();

        return transitioned;
    }

    private void SetDesiredState(bool desiredIsActive)
    {
        _desiredIsActive = desiredIsActive;
        _stateVersion++;
    }

    private bool TryStartPublication()
    {
        if (_isPublishing || _publishedStateVersion == _stateVersion)
            return false;

        _isPublishing = true;
        return true;
    }

    private void PublishObservableState()
    {
        ExceptionDispatchInfo? deferredException = null;
        while (true)
        {
            bool desiredIsActive;
            long stateVersion;
            lock (_gate)
            {
                desiredIsActive = _desiredIsActive;
                stateVersion = _stateVersion;
            }

            try
            {
                _setIsActive(desiredIsActive);
            }
            catch (Exception exception)
            {
                if (deferredException is not null)
                {
                    lock (_gate)
                        _isPublishing = false;

                    throw new AggregateException(
                        "Observable state publication failed while recovering from an earlier publication failure.",
                        deferredException.SourceException,
                        exception);
                }

                var capturedException = ExceptionDispatchInfo.Capture(exception);
                bool hasPendingPublication;
                lock (_gate)
                {
                    hasPendingPublication = _stateVersion != stateVersion
                        || _desiredIsActive != desiredIsActive;
                    if (!hasPendingPublication)
                        _isPublishing = false;
                }

                if (!hasPendingPublication)
                    capturedException.Throw();

                deferredException = capturedException;
                continue;
            }

            bool publicationConverged;
            lock (_gate)
            {
                _publishedStateVersion = stateVersion;
                publicationConverged = _stateVersion == stateVersion
                    && _desiredIsActive == desiredIsActive;
                if (publicationConverged)
                    _isPublishing = false;
            }

            if (!publicationConverged)
                continue;

            deferredException?.Throw();
            return;
        }
    }
}
