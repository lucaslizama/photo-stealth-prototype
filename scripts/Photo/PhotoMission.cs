using System.Collections.Generic;
using Godot;

namespace PhotoStealthPrototype.Photo;

/// <summary>
/// The shot list: tracks which subjects have a passing photo and when the run is
/// done. Coexists with <c>StealthDirector</c>, which keeps owning the fail path —
/// this only cares about winning.
/// </summary>
[GlobalClass]
public partial class PhotoMission : Node
{
    [Export] public PhotoCamera? Camera { get; set; }

    [Signal] public delegate void ProgressChangedEventHandler();
    [Signal] public delegate void MissionCompleteEventHandler();

    public IReadOnlyList<PhotoSubject> Subjects => _subjects;
    public bool Complete { get; private set; }

    public int CapturedCount
    {
        get
        {
            int count = 0;
            foreach (PhotoSubject subject in _subjects)
            {
                if (subject.Captured)
                {
                    count++;
                }
            }

            return count;
        }
    }

    private readonly List<PhotoSubject> _subjects = new();

    public override void _Ready()
    {
        // Deferred: subjects add themselves to the group in their own _Ready, and
        // sibling _Ready order is not guaranteed.
        CallDeferred(nameof(CollectSubjects));

        if (Camera is not null)
        {
            Camera.PhotoTaken += OnPhotoTaken;
        }
    }

    private void CollectSubjects()
    {
        _subjects.Clear();

        foreach (Node node in GetTree().GetNodesInGroup(PhotoSubject.GroupName))
        {
            if (node is PhotoSubject subject)
            {
                _subjects.Add(subject);
            }
        }

        if (_subjects.Count == 0)
        {
            GD.PushWarning("PhotoMission found no photo subjects — the run cannot be completed.");
        }

        EmitSignal(SignalName.ProgressChanged);
    }

    private void OnPhotoTaken()
    {
        EmitSignal(SignalName.ProgressChanged);

        if (Complete || _subjects.Count == 0 || CapturedCount < _subjects.Count)
        {
            return;
        }

        Complete = true;
        EmitSignal(SignalName.MissionComplete);
    }
}
