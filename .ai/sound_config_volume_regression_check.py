from pathlib import Path


ROOT = Path(__file__).resolve().parents[1]
sound_config = (ROOT / "Pal98Timer" / "SoundConfig.cs").read_text(encoding="utf-8")
sound_form = (ROOT / "Pal98Timer" / "SoundConfigForm.cs").read_text(encoding="utf-8")


def require(text: str, needle: str, label: str) -> None:
    if needle not in text:
        raise SystemExit(f"FAIL: missing {label}: {needle}")


require(sound_config, "private Dictionary<SoundTriggerType, int> _soundVolumes", "per-trigger volume storage")
require(sound_config, "private static int ClampVolume(int volume)", "volume clamp")
require(sound_config, "ApplyMciVolume(alias, volume)", "MCI runtime volume")
require(sound_config, "PlayMp3WithWmp(path, volume)", "WMP fallback volume")
require(sound_config, "触发类型=启用|音量(0-100)|文件路径", "new config format comment")
require(sound_config, "value.Split(new char[] { '|' }, 3)", "backward-compatible config split")
require(sound_config, "public void SetSoundVolume(SoundTriggerType type, int volume)", "volume setter")
require(sound_config, "public int GetSoundVolume(SoundTriggerType type)", "volume getter")
require(sound_config, "public void TestPlay(string filePath, int volumePercent = 100)", "preview volume")

require(sound_form, "private NumericUpDown[] numVolumes;", "per-trigger volume controls")
require(sound_form, "private NumericUpDown numSoundOnVolume;", "toggle-on volume control")
require(sound_form, "private NumericUpDown numSoundOffVolume;", "toggle-off volume control")
require(sound_form, "numVolumes[i].Value = sc.GetSoundVolume(types[i]);", "load per-trigger volume")
require(sound_form, "SoundConfig.ins.TestPlay(path, (int)numVolumes[index].Value);", "preview per-trigger volume")
require(sound_form, "sc.SetSoundVolume(types[i], (int)numVolumes[i].Value);", "save per-trigger volume")
require(sound_form, "sc.SoundEnabledOnVolume = (int)numSoundOnVolume.Value;", "save toggle-on volume")
require(sound_form, "sc.SoundEnabledOffVolume = (int)numSoundOffVolume.Value;", "save toggle-off volume")

print("PASS: PalTimer sound config supports per-audio volume with backward-compatible config parsing.")
