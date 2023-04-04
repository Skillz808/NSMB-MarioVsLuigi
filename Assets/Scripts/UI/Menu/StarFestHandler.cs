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
    public GameObject starfestPrompt;
    public GameObject teamImageOne;
    public GameObject teamImageTwo;
    public GameObject announcementImage1;
    public GameObject announcementImage2;
    public GameObject teamButtonOne;
    public GameObject teamButtonTwo;
    public GameObject teamName1;
    public GameObject teamName2;
    
    [Header("Other Properties")]
    public int chosenTeam = 0;
    public DateTime starfestStartTime;
    private TimeSpan timeRemaining;

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
            yield return request.SendWebRequest();

            // Check if there was an error with the request
            if (request.result != UnityWebRequest.Result.Success)
            {
                Debug.LogError(request.error);
                countdownText.text = "Cannot connect to Starfest server... Please spam Skillz#6262 on Discord until he fixes it!";
                yield break;
            }

            DateTime now = DateTime.Now;
            DateTime startTime = DateTime.MinValue;
            string team1Name = "Team 1";
            string team2Name = "Team 2";
            string teamColor1 = "#FFFFFF";
            string teamColor2 = "#FFFFFF";
            bool foundStarfest = false;

            // Parse the JSON response to extract the start time
            string responseText = request.downloadHandler.text;
            StarfestInfo starfestInfo = JsonUtility.FromJson<StarfestInfo>(responseText);
            
            // Loop through the starfests array
            foreach (var starfest in starfestInfo.starfests)
            {
                Debug.Log(starfest.endTime);
                
                // Parse the end time of the starfest
                DateTime endTime = DateTime.Parse(starfest.endTime);

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
            yield break;
        }
        else{
            teamImageOne.GetComponent<Graphic>().color = ColorUtility.TryParseHtmlString(teamColor1, out Color color1) ? color1 : Color.white;
            teamImageTwo.GetComponent<Graphic>().color = ColorUtility.TryParseHtmlString(teamColor2, out Color color2) ? color2 : Color.white;
            teamButtonOne.GetComponent<Graphic>().color = ColorUtility.TryParseHtmlString(teamColor1, out Color color3) ? color3 : Color.white;
            teamButtonTwo.GetComponent<Graphic>().color = ColorUtility.TryParseHtmlString(teamColor2, out Color color4) ? color4 : Color.white;
            announcementImage1.GetComponent<Graphic>().color = ColorUtility.TryParseHtmlString(teamColor1, out Color color5) ? color5 : Color.white;
            announcementImage2.GetComponent<Graphic>().color = ColorUtility.TryParseHtmlString(teamColor2, out Color color6) ? color6 : Color.white;
            //teamFlagOne.GetComponent<Graphic>().color = ColorUtility.TryParseHtmlString(teamColor1, out Color color5) ? color5 : Color.white;
            //teamFlagTwo.GetComponent<Graphic>().color = ColorUtility.TryParseHtmlString(teamColor2, out Color color6) ? color6 : Color.white;

            teamName1.GetComponent<TextMeshProUGUI>().text = team1Name;
            teamName2.GetComponent<TextMeshProUGUI>().text = team2Name;

            Debug.Log(startTime);

            // Calculate the time remaining until the starfest starts
            timeRemaining = startTime - DateTime.Now;

            Debug.Log(timeRemaining);

            // Start the countdown coroutine
            StartCoroutine(Countdown());
        }
    }
}

    IEnumerator IncrementTeam1CountCoroutine()
    {
        // Create the UnityWebRequest object with the PUT method
        var request = UnityWebRequest.Put("http://localhost:3000/starfests/increment-team1-count", "");

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

    IEnumerator Countdown()
    {
        Debug.Log("Countdown started");
        while (timeRemaining.TotalSeconds > 0)
        {
            // Update the countdown text
            if (timeRemaining.TotalSeconds < 60)
                countdownText.text = "Starfest starts in " + timeRemaining.Seconds + " seconds!";
            else if (timeRemaining.TotalSeconds < 3600)
                countdownText.text = "Starfest starts in " + timeRemaining.ToString(@"mm\:ss") + "!";
            else if (timeRemaining.TotalSeconds < 86400)
                countdownText.text = "Starfest starts in " + timeRemaining.ToString(@"hh\:mm\:ss") + "!";
            else if (timeRemaining.TotalSeconds < 604800) // 604800 seconds in a week
                countdownText.text = "Starfest starts in " + timeRemaining.Days + " days!";
            else if (timeRemaining.TotalSeconds < 2629743) // 2629743 seconds in a month
                countdownText.text = "Starfest starts in " + (timeRemaining.Days / 7) + " weeks!";
            else if (timeRemaining.TotalSeconds < 31556926) // 31556926 seconds in a year
                countdownText.text = "Starfest starts in " + (timeRemaining.Days / 30) + " months!";
            else
                countdownText.text = "Starfest starts in " + (timeRemaining.Days / 365) + " years!"; //Just in case :P 

            // Wait for 1 second before updating the countdown again
            yield return new WaitForSeconds(1f);

            // Decrement the time remaining by 1 second
            timeRemaining = timeRemaining.Subtract(TimeSpan.FromSeconds(1));
        }

        // Once the countdown reaches 0, start the starfest
        StartStarfest();
    }

    void StartStarfest()
    {
        countdownText.text = "Starfest has started!";
        starfestPrompt.SetActive(true);
    }

    public void ChooseTeamA()
    {
        chosenTeam = 0;
        StartCoroutine(IncrementTeam1CountCoroutine());
        starfestPrompt.SetActive(false);
    }

    public void ChooseTeamB()
    {
        chosenTeam = 1;
        StartCoroutine(IncrementTeam2CountCoroutine());
        starfestPrompt.SetActive(false);
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