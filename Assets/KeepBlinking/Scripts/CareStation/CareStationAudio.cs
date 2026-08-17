using UnityEngine;

namespace KeepBlinking.CareStation
{
  internal sealed class CareStationAudio : MonoBehaviour
  {
    private AudioSource _source;
    private AudioClip _workClip;

    internal void Build()
    {
      if (_source != null) return;
      _source = gameObject.AddComponent<AudioSource>();
      _source.playOnAwake = false;
      _source.loop = true;
      _source.spatialBlend = 0f;
      _source.volume = 0.12f;
      const int sampleRate = 24000;
      const float duration = 4f;
      var samples = Mathf.RoundToInt(sampleRate * duration);
      var data = new float[samples];
      for (var i = 0; i < samples; i++)
      {
        var t = i / (float)sampleRate;
        var slowPulse = 0.3f + 0.7f * Mathf.Pow(0.5f + 0.5f * Mathf.Sin(t * Mathf.PI * 0.8f), 3f);
        var pump = Mathf.Sin(t * Mathf.PI * 2f * 92f) * 0.035f;
        var crew = Mathf.Sin(t * Mathf.PI * 2f * 184f) * 0.012f;
        data[i] = (pump + crew) * slowPulse;
      }
      _workClip = AudioClip.Create("Care Crew Work Ambience", samples, 1, sampleRate, false);
      _workClip.SetData(data, 0);
      _source.clip = _workClip;
    }

    internal void StartWork(CareStationIncidentType incident)
    {
      Build();
      _source.pitch = incident == CareStationIncidentType.DrySpot ? 0.92f : incident == CareStationIncidentType.EyeGunk ? 1.08f : 1f;
      if (!_source.isPlaying) _source.Play();
    }

    internal void StopWork()
    {
      if (_source != null && _source.isPlaying) _source.Stop();
    }

    private void OnDestroy()
    {
      if (_workClip != null) Destroy(_workClip);
    }
  }
}
