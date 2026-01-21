using System.Collections.ObjectModel;
using Triominos.Models;

namespace Triominos.ViewModels;

/// <summary>
/// ViewModel wrapper for a Player, providing bindable properties
/// </summary>
public class PlayerViewModel : ViewModelBase
{
    private bool _isCurrentPlayer;
    private bool _isActive = true;

    public Player Player { get; }

    public int Id => Player.Id;
    
    public string Name
    {
        get => Player.Name;
        set
        {
            if (Player.Name != value)
            {
                Player.Name = value;
                OnPropertyChanged();
            }
        }
    }

    public int Score => Player.Score;

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

    public PlayerViewModel(Player player)
    {
        Player = player;
        
        // Sync pieces from model to viewmodels
        foreach (var piece in player.Rack)
        {
            RackPieces.Add(new TriominoPieceViewModel(piece));
        }
    }

    /// <summary>
    /// Adds a piece to the player's rack
    /// </summary>
    public void AddPiece(TriominoPiece piece)
    {
        Player.AddPiece(piece);
        RackPieces.Add(new TriominoPieceViewModel(piece));
        OnPropertyChanged(nameof(PiecesRemaining));
    }

    /// <summary>
    /// Adds an existing piece viewmodel to the player's rack
    /// </summary>
    public void AddPieceViewModel(TriominoPieceViewModel pieceVm)
    {
        Player.AddPiece(pieceVm.Piece);
        RackPieces.Add(pieceVm);
        OnPropertyChanged(nameof(PiecesRemaining));
    }

    /// <summary>
    /// Removes a piece from the player's rack
    /// </summary>
    public bool RemovePiece(TriominoPieceViewModel pieceVm)
    {
        if (Player.RemovePiece(pieceVm.Piece))
        {
            RackPieces.Remove(pieceVm);
            OnPropertyChanged(nameof(PiecesRemaining));
            return true;
        }
        return false;
    }

    /// <summary>
    /// Clears all pieces from the player's rack
    /// </summary>
    public void ClearRack()
    {
        Player.ClearRack();
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

    public override string ToString() => Player.ToString();
}
