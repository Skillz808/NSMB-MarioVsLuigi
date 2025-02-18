using NSMB.UI.MainMenu.Submenus;
using NSMB.Utils;
using Photon.Realtime;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using System.Linq;

namespace NSMB.UI.MainMenu {
    public class RoomListManager : MonoBehaviour, ILobbyCallbacks, IConnectionCallbacks {

        //---Properties
        private bool _filterFullRooms;
        public bool FilterFullRooms {
            get => _filterFullRooms;
            set {
                _filterFullRooms = value;
                RefreshRooms();
            }
        }
        private bool _filterInProgressRooms;
        public bool FilterInProgressRooms {
            get => _filterInProgressRooms;
            set {
                _filterInProgressRooms = value;
                RefreshRooms();
            }
        }

        //---Serialized Variables
        [SerializeField] private MainMenuCanvas canvas;
        [SerializeField] private RoomListSubmenu submenu;
        [SerializeField] private RoomIcon roomIconPrefab;
        [SerializeField] private GameObject roomListScrollRect, privateRoomIdPrompt;
        [SerializeField] private TMP_Text filterRoomCountText;

        //---Private Variables
        private readonly Dictionary<string, RoomIcon> rooms = new();
        private List<RoomInfo> quickplayRooms = new();

        public void Start() {
            NetworkHandler.Client.AddCallbackTarget(this);
            roomIconPrefab.gameObject.SetActive(false);
        }

        public void OnDestroy() {
            NetworkHandler.Client?.RemoveCallbackTarget(this);
        }

        public void RefreshRooms(bool updateUI = true) {
            int filtered = 0;
            foreach (RoomIcon room in rooms.Values) {
                if (updateUI) {
                    room.UpdateUI(room.room);
                }

                if (FilterFullRooms && room.room.PlayerCount == room.room.MaxPlayers) {
                    room.gameObject.SetActive(false);
                    filtered++;
                } else if (FilterInProgressRooms && room.HasGameStarted) {
                    room.gameObject.SetActive(false);
                    filtered++;
                } else {
                    room.gameObject.SetActive(true);
                }
            }

            filterRoomCountText.enabled = filtered > 0;
            filterRoomCountText.text = GlobalController.Instance.translationManager.GetTranslationWithReplacements("ui.rooms.hidden", "rooms", filtered.ToString());
            filterRoomCountText.transform.SetAsLastSibling();
        }

        private void CreateRoom(RoomInfo newRoomInfo) {
            RoomIcon roomIcon = Instantiate(roomIconPrefab, Vector3.zero, Quaternion.identity);
            roomIcon.name = newRoomInfo.Name;
            roomIcon.gameObject.SetActive(true);
            roomIcon.transform.SetParent(roomListScrollRect.transform, false);
            roomIcon.UpdateUI(newRoomInfo);

            rooms[newRoomInfo.Name] = roomIcon;
            filterRoomCountText.transform.SetAsLastSibling();
        }

        public void JoinRoom(RoomIcon room) {
            if (!Settings.Instance.generalNickname.IsValidUsername()) {
                submenu.InvalidUsername();
                return;
            }
            canvas.PlayConfirmSound();
            _ = NetworkHandler.JoinRoom(new EnterRoomArgs {
                RoomName = room.room.Name,
            });
        }

        private void RemoveRoom(RoomIcon icon) {
            if (canvas.EventSystem.currentSelectedGameObject == icon.gameObject) {
                // Move cursor so it doesn't get stuck.
                // TODO
            }

            Destroy(icon.gameObject);
            rooms.Remove(icon.room.Name);
        }

        public void ClearRooms() {
            foreach (RoomIcon room in rooms.Values) {
                Destroy(room.gameObject);
            }
            rooms.Clear();
            quickplayRooms.Clear();
            filterRoomCountText.enabled = false;
        }

        //---Callbacks
        public void OnJoinedLobby() { }

        public void OnLeftLobby() {
            ClearRooms();
        }

        public void OnRoomListUpdate(List<RoomInfo> roomList) {
            foreach (RoomInfo newRoomInfo in roomList) {
                string roomName = newRoomInfo.Name;

                // Check if it's a quickplay room using properties
                bool isQuickplay = NetworkUtils.GetBooleanProperties(
                    newRoomInfo.CustomProperties,
                    out NetworkUtils.BooleanProperties props) && props.QuickPlay;

                Debug.Log($"[RoomList] Room {roomName} - IsQuickplay: {isQuickplay}, Players: {newRoomInfo.PlayerCount}/{newRoomInfo.MaxPlayers}");

                if (isQuickplay) {
                    if (newRoomInfo.RemovedFromList) {
                        Debug.Log($"[RoomList] Removing quickplay room {roomName}");
                        quickplayRooms.RemoveAll(r => r.Name == roomName);
                    } else {
                        var existingIndex = quickplayRooms.FindIndex(r => r.Name == roomName);
                        if (existingIndex != -1) {
                            Debug.Log($"[RoomList] Updating existing quickplay room {roomName}");
                            quickplayRooms[existingIndex] = newRoomInfo;
                        } else {
                            Debug.Log($"[RoomList] Adding new quickplay room {roomName}");
                            quickplayRooms.Add(newRoomInfo);
                        }
                    }
                    continue;
                }

                if (rooms.TryGetValue(roomName, out RoomIcon roomIcon)) {
                    // RoomIcon exists
                    if (newRoomInfo.RemovedFromList) {
                        // But we shouldn't display it anymore.
                        RemoveRoom(roomIcon);
                    } else {
                        // And it should still exist
                        roomIcon.UpdateUI(newRoomInfo);
                    }
                } else {
                    // RoomIcon doesn't exist
                    if (!newRoomInfo.RemovedFromList) {
                        // And it should
                        CreateRoom(newRoomInfo);
                    }
                }
            }

            Debug.Log($"[RoomList] Current quickplay rooms: {quickplayRooms.Count}");
            foreach (var room in quickplayRooms) {
                Debug.Log($"[RoomList] -> {room.Name}: {room.PlayerCount}/{room.MaxPlayers}");
            }

            RefreshRooms(false);
        }

        public RoomInfo GetAvailableQuickplayRoom(int maxPlayers) {
            Debug.Log($"[RoomList] Searching for quickplay room with {maxPlayers} max players among {quickplayRooms.Count} rooms");

            var availableRoom = quickplayRooms.FirstOrDefault(r => {
                bool isQuickplay = NetworkUtils.GetBooleanProperties(
                    r.CustomProperties,
                    out NetworkUtils.BooleanProperties boolProps) && boolProps.QuickPlay;

                bool hasSpace = r.PlayerCount < r.MaxPlayers;
                bool matchesSize = r.MaxPlayers == maxPlayers;

                Debug.Log($"[RoomList] Checking room {r.Name}: QuickPlay={isQuickplay}, HasSpace={hasSpace}, MatchesSize={matchesSize}");

                return isQuickplay && hasSpace && matchesSize;
            });

            if (availableRoom != null) {
                Debug.Log($"[RoomList] Found matching room: {availableRoom.Name}");
            } else {
                Debug.Log("[RoomList] No matching room found");
            }

            return availableRoom;
        }

        public void OnLobbyStatisticsUpdate(List<TypedLobbyInfo> lobbyStatistics) { }

        public void OnConnected() { }

        public void OnConnectedToMaster() {
            ClearRooms();
        }

        public void OnDisconnected(DisconnectCause cause) {
            ClearRooms();
        }

        public void OnRegionListReceived(RegionHandler regionHandler) { }
        
        public void OnCustomAuthenticationResponse(Dictionary<string, object> data) { }

        public void OnCustomAuthenticationFailed(string debugMessage) { }
    }
}