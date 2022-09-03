using BattleTech;
using BattleTech.BinkMedia;
using BattleTech.Rendering;
using BattleTech.Save;
using BattleTech.UI;
using BinkPlugin;
using Harmony;
using HBS;
using Localize;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace CustomIntro {
  [HarmonyPatch(typeof(SimGameState), "PlayVideo")]
  [HarmonyPatch(MethodType.Normal)]
  [HarmonyPatch(new Type[] { typeof(string) })]
  public static class SimGameState_PlayVideo {
    public static bool Prefix(SimGameState __instance, string video) {
      try {
        if (Core.FindVideo(video, out string videopath, out string subtitlespath) == false) {
          return true;
        }
        SGVideoPlayer videoPlayer = __instance.GetVideoPlayer();
        videoPlayer.gameObject.SetActive(true);
        __instance.VideoPlayerActive = true;
        LazySingletonBehavior<UIManager>.Instance.StartCoroutine(videoPlayer.PauseGameAudio(AudioEventManager.AudioConstants.audioFadeDuration));
        videoPlayer.PlayCustomVideo(videopath, subtitlespath, new Action<string>(__instance.OnVideoComplete));
      } catch (Exception e) {
        Log.TWL(0, e.ToString(), true);
      }
      return true;
    }
  }
  [HarmonyPatch(typeof(SaveManager))]
  [HarmonyPatch(MethodType.Constructor)]
  [HarmonyPatch(new Type[] { typeof(MessageCenter) })]
  public static class SaveManager_Constructor {
    public static void Postfix(SaveManager __instance) {
      Core.LocalSettingsDir = Core.settings.useModtekFolder?Core.SearchCacheFolder():Path.GetDirectoryName(__instance.saveSystem.localWriteLocation.rootPath);
      if (string.IsNullOrEmpty(Core.LocalSettingsDir)) { Core.LocalSettingsDir = Path.GetDirectoryName(__instance.saveSystem.localWriteLocation.rootPath); };
      Log.TWL(0, "LocalSettingsDir:" + Core.LocalSettingsDir);
    }
  }
  [HarmonyPatch(typeof(IntroCinematicLauncher), "Init")]
  public static class IntroCinematicLauncher_Init_Patch {
    public static void Postfix() {
      Core.LoadPlayed();
      if ((Core.CheckAlreadyPlayed(Core.intro.currentIntro, Strings.CurrentCulture) == false) || (Core.settings.playEveryStart)) {
        Log.TWL(0, "Should Play",true);
        IntroCinematicLauncher.state = IntroCinematicLauncher.IntroPlayState.ShouldPlay;
      } else {
        Log.TWL(0, "Should not Play",true);
        IntroCinematicLauncher.state = IntroCinematicLauncher.IntroPlayState.HasPlayed;
      }
    }
  }
  [HarmonyPatch(typeof(IntroCinematicLauncher), "OnAddedToHierarchy")]
  public static class IntroCinematicLauncher_OnAddedToHierarchy {
    public static void PlayCustom(this BinkMediaPlayer binkPlayer, string videoPath, string subtitles) {
      try {
        binkPlayer.FinishedFrameOpenCheck = true;
        binkPlayer.VideoPath = videoPath;
        if (binkPlayer.VideoPath.Length != 0) {
          ulong file_byte_offset = 0;
          binkPlayer.bink = Bink.Open(binkPlayer.VideoPath, binkPlayer.soundOutput, binkPlayer.soundTrackOffset, binkPlayer.ioBuffering, file_byte_offset);
          if (binkPlayer.bink == IntPtr.Zero) {
            Log.TWL(0, "Bink error:" + Bink.GetError() + " " + binkPlayer.VideoPath, true);
          } else {
            binkPlayer.info = new Bink.Info();
            Bink.GetInfo(binkPlayer.bink, ref binkPlayer.info);
            binkPlayer.binkw = (float)binkPlayer.info.Width;
            binkPlayer.binkh = (float)binkPlayer.info.Height;
            Bink.Loop(binkPlayer.bink, (uint)binkPlayer.loopCount);
          }
        }
        if ((ActiveOrDefaultSettings.CloudSettings.subtitles) && (string.IsNullOrEmpty(subtitles) == false)) {
          binkPlayer.SubtitlePath = subtitles;
          if (binkPlayer.SubtitlePath.Length != 0) {
            string data = string.Empty;
            if (File.Exists(binkPlayer.SubtitlePath))
              data = File.ReadAllText(binkPlayer.SubtitlePath);
            binkPlayer.LoadSubtitlesSRT(data);
          }
        } else {
          binkPlayer.previousSubtitleIndex = -1;
          binkPlayer.LoadSubtitlesSRT(string.Empty);
        }
        if (BinkMediaPlayer.cr == null) {
          BinkMediaPlayer.cr = binkPlayer.StartCoroutine("EndOfFrame");
        }
        ++BinkMediaPlayer.cr_num;
      }catch(Exception e) {
        Log.TWL(0,e.ToString(),true);
      }
    }

    public static void PlayCustomVideo(this SGVideoPlayer videoPlayer, string video, string subtitles, Action<string> onComplete) {
      if (DebugBridge.DisableCinematics) {
        if (onComplete == null)
          return;
        onComplete(video);
      } else {
        videoPlayer.Initialize();
        videoPlayer.HideCursor();
        Log.TWL(0, "BinkMediaPlayer.PlayCustom");
        Log.WL(1, video);
        Log.WL(1, subtitles);
        videoPlayer.BinkMediaPlayer.PlayCustom(video, subtitles);
        videoPlayer.SetVolumeLevels();
        BTPostProcess.SetUIPostprocessing(false);
        videoPlayer.OnPlayerComplete = onComplete;
      }
    }
    public static bool Prefix(IntroCinematicLauncher __instance) {
      try {
        //base.OnAddedToHierarchy();
        Log.TWL(0, $"IntroCinematicLauncher.OnAddedToHierarchy",true);
        if (IntroCinematicLauncher.state == IntroCinematicLauncher.IntroPlayState.ShouldPlay) {
          IntroCinematicLauncher.state = IntroCinematicLauncher.IntroPlayState.Playing;
          __instance.VideoPlayer = LazySingletonBehavior<UIManager>.Instance.GetOrCreatePopupModule<SGVideoPlayer>();
          if (Core.FindVideo(Core.intro.currentIntro, out string videopath, out string subtitlespath)) {
            __instance.VideoPlayer.PlayCustomVideo(videopath, subtitlespath, new Action<string>(__instance.OnVideoComplete));
          } else {
            __instance.VideoPlayer.PlayVideo("0-opening.mp4", Strings.CurrentCulture, new Action<string>(__instance.OnVideoComplete));
          }
          Core.AddAlreadyPlayed(Core.intro.currentIntro, Strings.CurrentCulture);
          Core.SavePlayed();
        } else {
          __instance.Pool();
        }
        return false;
      } catch (Exception e) {
        Log.TWL(0, e.ToString(), true);
      }
      return true;
    }
  }
  public class Settings {
    public bool debugLog { get; set; } = true;
    public bool playEveryStart { get; set; } = false;
    public bool useModtekFolder { get; set; } = true;
  }
  public class IntroSettings {
    public string currentIntro { get; set; } = string.Empty;
  }
  public class PlayedIntrosDef {
    public HashSet<string> alreadyPlayed { get; set; } = new HashSet<string>();
  }
  public class IntroVideoFileDef {
    public string id { get; set; } = string.Empty;
    public Dictionary<Strings.Culture, string> videos { get; set; } = new Dictionary<Strings.Culture, string>();
    public Dictionary<Strings.Culture, string> subtitles { get; set; } = new Dictionary<Strings.Culture, string>();
  }
  public class Core {
    public static void LoadPlayed() {
      string path = Path.Combine(LocalSettingsDir, "intro_played.json");
      Log.TWL(0, $"LoadPlayed {path}");
      if (File.Exists(path)) {
        Log.WL(1, "exists");
        string content = File.ReadAllText(path);
        played = JsonConvert.DeserializeObject<PlayedIntrosDef>(content);
        Log.WL(0, content, true);
        Log.WL(0, JsonConvert.SerializeObject(played, Formatting.Indented),true);
      }
    }
    public static void SavePlayed() {
      string path = Path.Combine(LocalSettingsDir, "intro_played.json");
      File.WriteAllText(path, JsonConvert.SerializeObject(played, Formatting.Indented));
    }
    public static bool CheckAlreadyPlayed(string name, Strings.Culture culture) {
      string key = name + "_" + culture.ToString();
      Log.TWL(0, $"CheckAlreadyPlayed {key} exists:{played.alreadyPlayed.Contains(key)}");
      return played.alreadyPlayed.Contains(key);
    }
    public static void AddAlreadyPlayed(string name, Strings.Culture culture) {
      string key = name + "_" + culture.ToString();
      played.alreadyPlayed.Add(key);
    }
    public static PlayedIntrosDef played { get; set; } = new PlayedIntrosDef();
    public static HarmonyInstance harmony { get; set; } = null;
    public static string BaseDir { get; private set; }
    public static string LocalSettingsDir { get; set; } = string.Empty;
    public static Settings settings { get; set; } = new Settings();
    public static IntroSettings intro { get; set; } = new IntroSettings();
    public static Dictionary<string, IntroVideoFileDef> videoDefs { get; set; } = new Dictionary<string, IntroVideoFileDef>();
    public static Dictionary<string, string> videos { get; set; } = new Dictionary<string, string>();
    public static Dictionary<string, string> subtitles { get; set; } = new Dictionary<string, string>();
    public static bool FindVideo(string id, out string video, out string subtitles) {
      video = string.Empty;
      subtitles = string.Empty;
      if (Core.videoDefs.TryGetValue(id, out var curIntroDef) == false) {
        Log.WL(1, " can't find video definition", true);
        return false;
      }
      string subtitlespath = string.Empty;
      if (curIntroDef.videos.TryGetValue(Strings.CurrentCulture, out var videoname) == false) {
        if (curIntroDef.videos.TryGetValue(Strings.Culture.CULTURE_EN_US, out videoname) == false) {
          Log.WL(1, $" can't find either {Strings.CurrentCulture} either {Strings.Culture.CULTURE_EN_US} culture in definition for video", true);
          return false;
        }
      }
      if (curIntroDef.subtitles.TryGetValue(Strings.CurrentCulture, out var subtitlesname) == false) {
        if (curIntroDef.subtitles.TryGetValue(Strings.Culture.CULTURE_EN_US, out subtitlesname) == false) {
          Log.WL(1, $" can't find either {Strings.CurrentCulture} either {Strings.Culture.CULTURE_EN_US} culture in definition for subtitles", true);
        }
      }
      if (string.IsNullOrEmpty(videoname)) { return false; }
      if (Core.videos.TryGetValue(videoname, out var videopath) == false) {
        Log.WL(1, $" can't find video {videoname}", true);
        return false;
      }
      if (string.IsNullOrEmpty(subtitlesname) == false) {
        if (Core.videos.TryGetValue(subtitlesname, out subtitlespath) == false) {
          Log.WL(1, $" can't find video {subtitlesname}", true);
        }
      }
      Log.TWL(0, "playing custom");
      Log.WL(1, videopath, true);
      Log.WL(1, subtitlespath, true);
      video = videopath;
      subtitles = subtitlespath;
      return true;
    }
    public static void FinishedLoading(List<string> loadOrder, Dictionary<string, Dictionary<string, VersionManifestEntry>> customResources) {
      Log.TWL(0, "FinishedLoading", true);
      try {
        foreach (var customResource in customResources) {
          Log.TWL(0, "customResource:" + customResource.Key);
          if (customResource.Key == nameof(IntroVideoFileDef)) {
            foreach (var entry in customResource.Value) {
              try {
                IntroVideoFileDef videoDef = JsonConvert.DeserializeObject<IntroVideoFileDef>(File.ReadAllText(entry.Value.FilePath));
                if (videoDefs.ContainsKey(videoDef.id)) { videoDefs[videoDef.id] = videoDef; } else { videoDefs.Add(videoDef.id, videoDef); }
                Log.WL(1, $"{videoDef.id} {entry.Value.FilePath}");
              } catch (Exception e) {
                Log.TWL(0, e.ToString(), true);
              }
            }
          }else if(customResource.Key == "IntroVideoFile") {
            foreach (var entry in customResource.Value) {
              try {
                videos[entry.Key] = entry.Value.FilePath;
                Log.WL(1, $"{entry.Key} {entry.Value.FilePath}");
              } catch (Exception e) {
                Log.TWL(0, e.ToString(), true);
              }
            }
          } else if (customResource.Key == "IntroSubtitles") {
            foreach (var entry in customResource.Value) {
              try {
                subtitles[entry.Key] = entry.Value.FilePath;
                Log.WL(1, $"{entry.Key} {entry.Value.FilePath}");
              } catch (Exception e) {
                Log.TWL(0, e.ToString(), true);
              }
            }
          }
        }
        try {
          harmony = HarmonyInstance.Create("io.kmission.customintro");
          harmony.PatchAll(Assembly.GetExecutingAssembly());
        } catch (Exception e) {
          Log.TWL(0, e.ToString(), true);
        }
      } catch (Exception e) {
        Log.TWL(0, e.ToString(), true);
      }
    }
    public static string SearchCacheFolder() {
      string cur_dir = Core.BaseDir;
      while (string.IsNullOrEmpty(cur_dir) == false) {
        string cache_dir = Path.Combine(cur_dir, ".modtek");
        if (Directory.Exists(cache_dir)) { return cache_dir; }
        cur_dir = Path.GetDirectoryName(cur_dir);
      }
      return string.Empty;
    }
    public static void Init(string directory, string settingsJson) {
      Log.BaseDirectory = directory;
      Log.InitLog();
      Core.BaseDir = directory;
      Core.settings = JsonConvert.DeserializeObject<CustomIntro.Settings>(settingsJson);
      Core.intro = JsonConvert.DeserializeObject<CustomIntro.IntroSettings>(File.ReadAllText(Path.Combine(directory,"intro.json")));
      Log.TWL(0, "Initing... " + directory + " version: " + Assembly.GetExecutingAssembly().GetName().Version, true);
    }
  }
}

