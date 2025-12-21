using Comfort.Common;
using EFT.UI;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Networking;

namespace friendlySAIN.Modules
{
    internal class RadioSound : MonoBehaviour
    {


        private AudioClip radioClip;
        private AudioClip locationClip;

        public async void Enable()
        {

            List<AudioClip> sounds = await GetSound();
            radioClip = sounds[0];
            locationClip = sounds[1];
        }

        private async Task<List<AudioClip>> GetSound()
        {
            List<AudioClip> audioClips = new List<AudioClip>();
            string dllPath = Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location);

            string soundFilePath = Path.Combine(dllPath, "radiochat.ogg");

            audioClips.Add(await LoadAudioClip("file://" + soundFilePath));

            soundFilePath = Path.Combine(dllPath, "locationping.ogg");
            audioClips.Add(await LoadAudioClip("file://" + soundFilePath));

            return audioClips;
        }


        public static async Task<AudioClip> LoadAudioClip(string uri)
        {

            using (UnityWebRequest req = UnityWebRequestMultimedia.GetAudioClip(uri, AudioType.OGGVORBIS))
            {
                var asyncOperation = req.SendWebRequest();

                while (!asyncOperation.isDone) await Task.Yield();

                if (!req.isNetworkError && !req.isHttpError)
                {
                    var result = DownloadHandlerAudioClip.GetContent(req);

                    result.LoadAudioData();
                    while (result.loadState != AudioDataLoadState.Loaded) await Task.Yield();
                    return result;
                }
                else
                {
                    Logger.LogError($"Failed to load audio file: {req.error}");
                    return null;
                }
            }
        }

        public async void PlayRadioSound()
        {
            try
            {
                if (radioClip == null) return;

                float level = (friendlySAIN.statusSound.Value / 100f) * 0.5f;
                // try to fetch the sound again if it's not loaded
                if (radioClip.length == 0)
                {
                    string dllPath = Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location);
                    string soundFilePath = Path.Combine(dllPath, "radiochat.ogg");
                    radioClip = await LoadAudioClip("file://" + soundFilePath);
                }

                Singleton<GUISounds>.Instance.PlaySound(radioClip, false, true, level);
            }
            catch (System.Exception e)
            {
                Logger.LogError(e);
            }
        }

        public async void PlayLocationSound(float pan)
        {
            if (locationClip == null) return;
            try
            {
                float level = (friendlySAIN.statusSound.Value / 100f) * 0.6f;
                // try to fetch the sound again if it's not loaded
                if (locationClip.length == 0)
                {
                    string dllPath = Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location);
                    string soundFilePath = Path.Combine(dllPath, "locationping.ogg");
                    locationClip = await LoadAudioClip("file://" + soundFilePath);
                }

                Singleton<BetterAudio>.Instance.PlayNonspatial(locationClip, BetterAudio.AudioSourceGroupType.Nonspatial, pan, level);
            }
            catch (System.Exception e)
            {
                Logger.LogError(e);
            }
        }
    }

}