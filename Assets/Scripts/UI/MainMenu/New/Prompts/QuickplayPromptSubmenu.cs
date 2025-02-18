using Quantum;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Photon.Realtime;

namespace NSMB.UI.MainMenu.Submenus.Prompts {
    public class QuickplayPromptSubmenu : PromptSubmenu {
        //---Serialized Variables
        [SerializeField] private UnityEngine.UI.Button oneVsOneButton;
        [SerializeField] private UnityEngine.UI.Button twoVsTwoButton;
        [SerializeField] private UnityEngine.UI.Button fourVsFourButton;
        
        //---Private Variables
        private bool success;
        
        public override void Initialize() {
            base.Initialize();
            QuantumCallback.Subscribe<CallbackLocalPlayerAddConfirmed>(this, OnLocalPlayerAddConfirmed);
            
            // Set up button click handlers
            oneVsOneButton.onClick.AddListener(() => StartQuickPlay(2));  // 1v1 = 2 players
            twoVsTwoButton.onClick.AddListener(() => StartQuickPlay(4));  // 2v2 = 4 players
            fourVsFourButton.onClick.AddListener(() => StartQuickPlay(8)); // 4v4 = 8 players
        }

        public override void Show(bool first) {
            base.Show(first);
            success = false;
        }

        public override bool TryGoBack(out bool playSound) {
            if (success) {
                playSound = false;
                return true;
            }
            return base.TryGoBack(out playSound);
        }

        private void StartQuickPlay(int playerCount) {
            success = true;
            Canvas.PlayConfirmSound();
            
            _ = NetworkHandler.QuickPlay(new EnterRoomArgs {
                RoomOptions = new RoomOptions {
                    MaxPlayers = playerCount,
                    IsVisible = true
                }
            });
            
            Canvas.GoBack();
        }

        private void OnLocalPlayerAddConfirmed(CallbackLocalPlayerAddConfirmed e) {
            if (success && e.PlayerSlot == 0) {
                // No need to set visibility here since quickplay rooms are always visible
            }
        }
    }
}