using System;
using UnityEngine;

namespace SmashHit.Gameplay
{
    public enum GameState
    {
        Playing,
        Lost
    }

    public class GameStateController : MonoBehaviour
    {
        public GameState CurrentState { get; private set; } = GameState.Playing;

        public event Action<GameState, string> StateChanged;

        public bool IsPlaying => CurrentState == GameState.Playing;

        public void ResetGame()
        {
            CurrentState = GameState.Playing;
            StateChanged?.Invoke(CurrentState, string.Empty);
        }

        public void Lose(string reason)
        {
            if (CurrentState == GameState.Lost)
            {
                return;
            }

            CurrentState = GameState.Lost;
            Debug.Log($"Game over: {reason}");
            StateChanged?.Invoke(CurrentState, reason);
        }
    }
}
