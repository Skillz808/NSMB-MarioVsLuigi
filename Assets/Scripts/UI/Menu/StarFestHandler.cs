using System;
using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.UI;

public class StarFestHandler : MonoBehaviour //This script handles everything to do with starfests, at least in the main menu.
{
    [Header("UI References")]
    public TextMeshProUGUI countdownText;
    public TextMeshProUGUI teamText;
    public GameObject starfestPrompt;
    public GameObject teamImageOne;
    public GameObject teamImageTwo;
    public GameObject teamSliceOne;
    public GameObject teamSliceTwo;
    public GameObject announcementImage1;
    public GameObject announcementImage2;
    public GameObject teamButtonOne;
    public GameObject teamButtonTwo;
    public GameObject teamName1;
    public GameObject teamName2;
    public GameObject teamSliceOneMenu;
    public GameObject teamSliceTwoMenu;
    public GameObject starfestRoomButton;
    
    
    
    [Header("Other Properties")]
    public int chosenTeam = 0;
    public DateTime starfestStartTime;
    DateTime startTime = DateTime.MinValue;
    DateTime endTime = DateTime.MinValue;
    public Team[]           teams;
    private TimeSpan timeRemainingToStart;
    private TimeSpan timeRemainingToFinish;
    string teamColor1 = "#FFFFFF";
    string teamColor2 = "#FFFFFF";
    string team1Name = "Team 1";
    string team2Name = "Team 2";
    

    void Start()
    {
        // Send an HTTP GET request to the API endpoint to retrieve the starfest info
        StartCoroutine(GetStarfestInfo());
    }

    IEnumerator GetStarfestInfo()
    {
        // Send the GET request to the API endpoint
        using (UnityWebRequest request = UnityWebRequest.Get("http://localhost:3000/api/starfests"))
        {
            request.SetRequestHeader("x-api-key", "kD19^94ZttBJ!tq!UFj!Q");
            yield return request.SendWebRequest();

            // Check if there was an error with the request
            if (request.result != UnityWebRequest.Result.Success)
            {
                Debug.LogError(request.error);
                countdownText.text = "Failed to connect to the Starfest API...";
                starfestRoomButton.SetActive(false);
                yield break;
            }

            DateTime now = DateTime.Now;
            bool foundStarfest = false;

            // Parse the JSON response to extract the start time
            string responseText = request.downloadHandler.text;
            StarfestInfo starfestInfo = JsonUtility.FromJson<StarfestInfo>(responseText);
            
            // Loop through the starfests array
            foreach (var starfest in starfestInfo.starfests)
            {
                
                // Parse the end time of the starfest
                endTime = DateTime.Parse(starfest.endTime);

                // Check if the end time of the starfest is after the current time
                if (endTime > now)
                {
                    startTime = DateTime.Parse(starfest.startTime);
                    team1Name = starfest.team1Name;
                    team2Name = starfest.team2Name;
                    teamColor1 = starfest.team1Color;
                    teamColor2 = starfest.team2Color;

                    // Set the foundStarfest flag to true
                    foundStarfest = true;

                    // Exit the loop since we have found the next starfest
                    break;
                }
            }

        if (!foundStarfest)
        {
            // There are no starfests with an end date after the current time
            countdownText.text = "There are no Starfests scheduled at this time...";
                SetGraphicColor(teamImageOne, "#F0C000");
                SetGraphicColor(teamImageTwo, "#F0C000");
                SetGraphicColor(teamButtonOne, "#F0C000");
                SetGraphicColor(teamButtonTwo, "#F0C000");
                SetGraphicColor(announcementImage1, "#F0C000");
                SetGraphicColor(announcementImage2, "#F0C000");
                SetGraphicColor(teamSliceOne, "#F0C000");
                SetGraphicColor(teamSliceTwo, "#F0C000");
                SetGraphicColor(teamSliceOneMenu, "#F0C000");
                SetGraphicColor(teamSliceTwoMenu, "#F0C000");
                if (ColorUtility.TryParseHtmlString("#F0C000", out Color newColor1)) teams[0].color = newColor1;
                if (ColorUtility.TryParseHtmlString("#F0C000", out Color newColor2)) teams[1].color = newColor2;
                starfestRoomButton.SetActive(false);
            yield break;
        }
        else{
                SetGraphicColor(teamImageOne, teamColor1);
                SetGraphicColor(teamImageTwo, teamColor2);
                SetGraphicColor(teamButtonOne, teamColor1);
                SetGraphicColor(teamButtonTwo, teamColor2);
                SetGraphicColor(announcementImage1, teamColor1);
                SetGraphicColor(announcementImage2, teamColor2);
                SetGraphicColor(teamSliceOne, teamColor1);
                SetGraphicColor(teamSliceTwo, teamColor2);
                SetGraphicColor(teamSliceOneMenu, teamColor1);
                SetGraphicColor(teamSliceTwoMenu, teamColor2);
                if (ColorUtility.TryParseHtmlString(teamColor1, out Color newColor1)) teams[0].color = newColor1;
                if (ColorUtility.TryParseHtmlString(teamColor2, out Color newColor2)) teams[1].color = newColor2;

            teamName1.GetComponent<TextMeshProUGUI>().text = team1Name;
            teamName2.GetComponent<TextMeshProUGUI>().text = team2Name;

            Debug.Log(startTime);

            // Calculate the time remaining until the starfest starts
            StartCoroutine(CalculateTimeUntilStart());
            StartCoroutine(CalculateTimeRemaining());

            // Start the countdown coroutine
        }
    }
}
    IEnumerator countdownToStart()
    {
        Debug.Log("Countdown started");
        Debug.Log(timeRemainingToStart);
        while (timeRemainingToStart.TotalSeconds > 0)
        {
            // Update the countdown text
            if (timeRemainingToStart.TotalSeconds < 60)
                countdownText.text = "Starfest starts in " + timeRemainingToStart.Seconds + " seconds!";
            else if (timeRemainingToStart.TotalSeconds < 3600)
                countdownText.text = "Starfest starts in " + timeRemainingToStart.ToString(@"mm\:ss") + "!";
            else if (timeRemainingToStart.TotalSeconds < 86400)
                countdownText.text = "Starfest starts in " + timeRemainingToStart.ToString(@"hh\:mm\:ss") + "!";
            else if (timeRemainingToStart.TotalSeconds < 604800) // 604800 seconds in a week
                countdownText.text = "Starfest starts in " + timeRemainingToStart.Days + " days!";
            else if (timeRemainingToStart.TotalSeconds < 2629743) // 2629743 seconds in a month
                countdownText.text = "Starfest starts in " + (timeRemainingToStart.Days / 7) + " weeks!";
            else if (timeRemainingToStart.TotalSeconds < 31556926) // 31556926 seconds in a year
                countdownText.text = "Starfest starts in " + (timeRemainingToStart.Days / 30) + " months!";
            else
                countdownText.text = "Starfest starts in " + (timeRemainingToStart.Days / 365) + " years!"; //Just in case :P 

            // Wait for 1 second before updating the countdown again
            yield return new WaitForSeconds(1f);

            // Decrement the time remaining by 1 second
            timeRemainingToStart = timeRemainingToStart.Subtract(TimeSpan.FromSeconds(1));
        }

        if(timeRemainingToStart.TotalSeconds <= 0){
            print(timeRemainingToStart.TotalSeconds);
            startStarfest();
        }

        // Once the countdown reaches 0, start the starfest

    }

    IEnumerator countdownToFinish()
    {
        Debug.Log("Countdown started");
        while (timeRemainingToFinish.TotalSeconds > 0)
        {
            // Update the countdown text
            if (timeRemainingToFinish.TotalSeconds < 60)
                countdownText.text = "Starfest ends in " + timeRemainingToFinish.Seconds + " seconds!";
            else if (timeRemainingToFinish.TotalSeconds < 3600)
                countdownText.text = "Starfest ends in " + timeRemainingToFinish.ToString(@"mm\:ss") + "!";
            else if (timeRemainingToFinish.TotalSeconds < 86400)
                countdownText.text = "Starfest ends in " + timeRemainingToFinish.ToString(@"hh\:mm\:ss") + "!";
            else if (timeRemainingToFinish.TotalSeconds < 604800) // 604800 seconds in a week
                countdownText.text = "Starfest ends in " + timeRemainingToFinish.Days + " days!";
            else if (timeRemainingToFinish.TotalSeconds < 2629743) // 2629743 seconds in a month
                countdownText.text = "Starfest ends in " + (timeRemainingToFinish.Days / 7) + " weeks!";
            else if (timeRemainingToFinish.TotalSeconds < 31556926) // 31556926 seconds in a year
                countdownText.text = "Starfest ends in " + (timeRemainingToFinish.Days / 30) + " months!";
            else
                countdownText.text = "Starfest ends in " + (timeRemainingToFinish.Days / 365) + " years!"; //Just in case :P 

            // Wait for 1 second before updating the countdown again
            yield return new WaitForSeconds(1f);

            // Decrement the time remaining by 1 second
            timeRemainingToFinish = timeRemainingToFinish.Subtract(TimeSpan.FromSeconds(1));
        }

        // Once the countdown reaches 0, end the starfest
        endStarfest();
    }

    IEnumerator IncrementTeam1CountCoroutine()
    {
        // Create the UnityWebRequest object with the PUT method
        var request = UnityWebRequest.Put("http://localhost:3000/starfests/increment-team1-count", "");
        request.SetRequestHeader("x-api-key", "kD19^94ZttBJ!tq!UFj!Q");

        // Send the request
        yield return request.SendWebRequest();

        // Check for errors
        if (request.result == UnityWebRequest.Result.ConnectionError ||
            request.result == UnityWebRequest.Result.ProtocolError)
        {
            Debug.LogError(request.error);
        }
        else
        {
            Debug.Log("Team 1 count incremented successfully");
        }
    }

    IEnumerator IncrementTeam2CountCoroutine()
    {
        // Create the UnityWebRequest object with the PUT method
        var request = UnityWebRequest.Put("http://localhost:3000/starfests/increment-team2-count", "");
        request.SetRequestHeader("x-api-key", "kD19^94ZttBJ!tq!UFj!Q");

        // Send the request
        yield return request.SendWebRequest();

        // Check for errors
        if (request.result == UnityWebRequest.Result.ConnectionError ||
            request.result == UnityWebRequest.Result.ProtocolError)
        {
            Debug.LogError(request.error);
        }
        else
        {
            Debug.Log("Team 2 count incremented successfully");
        }
    }

IEnumerator CalculateTimeUntilStart() {
    // Retrieve the current date and time from the API
    UnityWebRequest request = UnityWebRequest.Get("http://localhost:3000/api/currentDateTime");
    request.SetRequestHeader("x-api-key", "kD19^94ZttBJ!tq!UFj!Q");
    yield return request.SendWebRequest();

    if (request.result == UnityWebRequest.Result.ConnectionError || request.result == UnityWebRequest.Result.ProtocolError) {
        Debug.Log(request.error);
    } else {
        // Calculate the time remaining to start the starfest
        string dateTimeString = request.downloadHandler.text;
        DateTime currentDateTime = DateTime.Parse(dateTimeString);
        timeRemainingToStart = startTime.Subtract(currentDateTime);

        Debug.Log("Time remaining to start the starfest: " + timeRemainingToStart.ToString());
    }
    StartCoroutine(countdownToStart());
}

IEnumerator CalculateTimeRemaining() {
    // Retrieve the current date and time from the API
    UnityWebRequest request = UnityWebRequest.Get("http://localhost:3000/api/currentDateTime");
    request.SetRequestHeader("x-api-key", "kD19^94ZttBJ!tq!UFj!Q");
    yield return request.SendWebRequest();

    if (request.result == UnityWebRequest.Result.ConnectionError || request.result == UnityWebRequest.Result.ProtocolError) {
        Debug.Log(request.error);
    } else {
        // Calculate the time remaining to finish the starfest
        string dateTimeString = request.downloadHandler.text;
        DateTime currentDateTime = DateTime.Parse(dateTimeString);
        Debug.Log(currentDateTime);
        Debug.Log(endTime);
        timeRemainingToFinish = endTime.Subtract(currentDateTime);

        Debug.Log("Time remaining to finish the starfest: " + timeRemainingToFinish.ToString());
    }
}

    void startStarfest()
    {
        StartCoroutine(countdownToFinish());
        starfestRoomButton.SetActive(true);
        print("I have been called!!!!!");
    }
    void endStarfest()
    {
        StartCoroutine(GetStarfestInfo());
        starfestPrompt.SetActive(false);
        starfestRoomButton.SetActive(false);
    }
    public void ChooseTeamA()
    {
        chosenTeam = 1;
        StartCoroutine(IncrementTeam1CountCoroutine());
        SetGraphicColor(starfestRoomButton, teamColor1);
        teamText.text = "(Team " + team1Name + ")";
        starfestPrompt.SetActive(false);
    }

    public void ChooseTeamB()
    {
        chosenTeam = 2;
        StartCoroutine(IncrementTeam2CountCoroutine());
        SetGraphicColor(starfestRoomButton, teamColor2);
        teamText.text = "(Team " + team2Name + ")";
        starfestPrompt.SetActive(false);
    }

    public void openTeamChooser()
    {
        if (chosenTeam == 0)
        {
            starfestPrompt.SetActive(true);
        }
    }

    private void SetGraphicColor(GameObject obj, string colorString)
    {
        ColorUtility.TryParseHtmlString(colorString, out Color color);
        obj.GetComponent<Graphic>().color = color;
    }

[System.Serializable]
public class StarfestInfo
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
    public string team1Color;
    public string team2Color;
    public int team1Count;
    public int team2Count;
    public int team1Points;
    public int team2Points;
}
}