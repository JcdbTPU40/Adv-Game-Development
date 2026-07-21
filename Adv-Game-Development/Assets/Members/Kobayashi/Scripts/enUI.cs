using TMPro;
using UnityEngine;
public class enUI : MonoBehaviour
{
    private TMP_Text enText;
    int score = ScoreManager.Instance.En;
    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        enText = GetComponent<TMP_Text>();
        enText.text = score.ToString()+"en";
    }

    // Update is called once per frame
    void Update()
    {
        int score = ScoreManager.Instance.En;
        enText.text = score.ToString() + "en";
    }
}
