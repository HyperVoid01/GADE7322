using UnityEngine;

public class MainMenuManager : MonoBehaviour
{
    [SerializeField] private GameObject mainMenu;
    [Tooltip("Build index of the game scene")]
    [SerializeField] private int gameSceneIndex = 1;

    public void PlayGame()
    {
        // Ignore extra clicks while a load is already underway
        if (LoadingScreen.IsLoading) return;

        mainMenu.SetActive(false);

        // The loading screen covers the scene change and the map generation, then fades into the game
        LoadingScreen.LoadScene(gameSceneIndex);
    }

    public void QuitGame()
    {
        Application.Quit();
    }
}