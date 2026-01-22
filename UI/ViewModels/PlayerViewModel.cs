using System.Collections.ObjectModel;
using Triominos.Core.Interfaces;
using Triominos.UI.Models;

namespace Triominos.UI.ViewModels;

/// <summary>
/// ViewModel wrapper for a Player, providing bindable properties
/// </summary>
public class PlayerViewModel : ViewModelBase
{
    private bool _isCurrentPlayer;
    private bool _isActive = true;
    private readonly IPlayer _corePlayer;

    public IPlayer CorePlayer => _corePlayer;

    public int Id => _corePlayer.Id;
    
    public string Name
    {
        get => _corePlayer.Name;
        set
        {
            if (_corePlayer.Name != value)
            {
                _corePlayer.Name = value;
                OnPropertyChanged();
            }
        }
    }

    public int Score => _corePlayer.Score;

    public ObservableCollection<TriominoPieceViewModel> RackPieces { get; } = [];

    public bool IsCurrentPlayer
    {
        get => _isCurrentPlayer;
        set => SetProperty(ref _isCurrentPlayer, value);
    }

    public bool IsActive
    {
        get => _isActive;
        set => SetProperty(ref _isActive, value);
    }

    public int PiecesRemaining => RackPieces.Count;

    public PlayerViewModel(IPlayer player)
    {
        _corePlayer = player;
        
        // Sync pieces from model to viewmodels
        foreach (var piece in player.Rack)
        {
            RackPieces.Add(new TriominoPieceViewModel(piece));
        }
    }

    /// <summary>
    /// Adds a piece to the player's rack (UI only - core is managed by engine)
    /// </summary>
    public void AddPieceViewModel(TriominoPieceViewModel pieceVm)
    {
        RackPieces.Add(pieceVm);
        OnPropertyChanged(nameof(PiecesRemaining));
    }

    /// <summary>
    /// Removes a piece from the player's rack (UI only)
    /// </summary>
    public bool RemovePieceViewModel(TriominoPieceViewModel pieceVm)
    {
        if (RackPieces.Remove(pieceVm))
        {
            OnPropertyChanged(nameof(PiecesRemaining));
            return true;
        }
        return false;
    }

    /// <summary>
    /// Clears all pieces from the player's rack (UI only)
    /// </summary>
    public void ClearRack()
    {
        RackPieces.Clear();
        OnPropertyChanged(nameof(PiecesRemaining));
    }

    /// <summary>
    /// Updates the score property notification
    /// </summary>
    public void RefreshScore()
    {
        OnPropertyChanged(nameof(Score));
    }

    public override string ToString() => $"{Name} (Score: {Score})";
}
