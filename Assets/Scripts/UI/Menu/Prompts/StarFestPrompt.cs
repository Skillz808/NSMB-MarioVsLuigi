using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.Networking;

public class TeamNameFetcher : MonoBehaviour
{
    public TextMeshProUGUI teamANameText;
    public TextMeshProUGUI teamBNameText;
    public int chosenTeam; // 0 for team A, 1 for team B

    IEnumerator GetTeamNames()
    {
        // Send a GET request to the API endpoint that retrieves the team names
        using (UnityWebRequest webRequest = UnityWebRequest.Get("http://localhost:3000/api/starfests"))
        {
            yield return webRequest.SendWebRequest();

            // If the request was successful, parse the JSON response to retrieve the team names
            if (webRequest.result == UnityWebRequest.Result.Success)
            {
                Debug.Log("GET request sent successfully");

                string jsonResponse = webRequest.downloadHandler.text;
                TeamNamesResponse response = JsonUtility.FromJson<TeamNamesResponse>(jsonResponse);

                // Set the TextMeshPro text to the retrieved team names
                teamANameText.text = response.starfests[0].team1Name;
                teamBNameText.text = response.starfests[0].team2Name;
            }
            else
            {
                Debug.LogError(webRequest.error);
            }
        }
    }

IEnumerator UpdateTeamCount(int teamCount)
{
    string teamName = chosenTeam == 0 ? teamANameText.text : teamBNameText.text;

    Debug.Log("Team name: " + teamName);

    // Send a GET request to the API endpoint to retrieve the current team count
    using (UnityWebRequest webRequest = UnityWebRequest.Get("http://localhost:3000/api/starfests")){
    webRequest.timeout = 10;
    yield return webRequest.SendWebRequest();

    if (webRequest.result == UnityWebRequest.Result.Success)
    {
        Debug.Log("GET request sent successfully");

        string jsonResponse = webRequest.downloadHandler.text;
        TeamNamesResponse response = JsonUtility.FromJson<TeamNamesResponse>(jsonResponse);

        int currentTeamCount = chosenTeam == 0 ? response.starfests[0].team1Count : response.starfests[0].team2Count;
        Debug.Log("Current team count: " + currentTeamCount);

        // Update the team count in the JSON file
        string putData = "{\"teamName\":\"" + teamName + "\",\"teamCount\":" + (currentTeamCount + teamCount) + "}";
        byte[] putDataBytes = System.Text.Encoding.UTF8.GetBytes(putData);

        UnityWebRequest putRequest = UnityWebRequest.Put("http://localhost:3000/api/starfests/teamCount", putDataBytes);
        putRequest.method = UnityWebRequest.kHttpVerbPUT;
        putRequest.SetRequestHeader("Content-Type", "application/json");

        yield return putRequest.SendWebRequest();

        if (putRequest.result == UnityWebRequest.Result.Success)
        {
            Debug.Log("PUT request sent successfully");
            Debug.Log("Team count updated successfully");
        }
        else
        {
            Debug.LogError("Failed to update team count: " + putRequest.error);
        }
    }
    else
    {
        Debug.LogError("Failed to get current team count: " + webRequest.error);
    }
}
}

    public void ChooseTeamA()
    {
        chosenTeam = 0;
        StartCoroutine(UpdateTeamCount(0));
        gameObject.SetActive(false);
    }

    public void ChooseTeamB()
    {
        chosenTeam = 1;
        StartCoroutine(UpdateTeamCount(1));
        gameObject.SetActive(false);
    }

    void Start()
    {
        // Call the GetTeamNames coroutine when the script starts
        StartCoroutine(GetTeamNames());
    }

    // A class to hold the retrieved team names
    [System.Serializable]
    private class TeamNamesResponse
    {
        public Starfest[] starfests;
    }

    [System.Serializable]
    public class Starfest
    {
        public int id;
        public string startTime;
        public string endTime;
        public string team1Name;
        public string team2Name;
        public int team1Count;
        public int team2Count;
        public int team1Points;
        public int team2Points;
    }
}
