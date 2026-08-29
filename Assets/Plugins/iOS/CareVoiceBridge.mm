#import <AVFoundation/AVFoundation.h>

static AVSpeechSynthesizer *CareVoiceSynthesizer(void)
{
  static AVSpeechSynthesizer *synthesizer = nil;
  static dispatch_once_t onceToken;
  dispatch_once(&onceToken, ^{ synthesizer = [[AVSpeechSynthesizer alloc] init]; });
  return synthesizer;
}

extern "C" void CareVoiceSpeak(const char *utf8Text, float rate, float pitch, float volume)
{
  if (utf8Text == NULL) return;
  NSString *text = [NSString stringWithUTF8String:utf8Text];
  if (text.length == 0) return;
  AVSpeechSynthesizer *synthesizer = CareVoiceSynthesizer();
  [synthesizer stopSpeakingAtBoundary:AVSpeechBoundaryImmediate];
  AVSpeechUtterance *utterance = [AVSpeechUtterance speechUtteranceWithString:text];
  utterance.voice = [AVSpeechSynthesisVoice voiceWithLanguage:@"en-US"];
  utterance.rate = MAX(0.3f, MIN(rate, 0.6f));
  utterance.pitchMultiplier = MAX(0.8f, MIN(pitch, 1.2f));
  utterance.volume = MAX(0.0f, MIN(volume, 1.0f));
  [synthesizer speakUtterance:utterance];
}

extern "C" void CareVoiceStop(void)
{
  [CareVoiceSynthesizer() stopSpeakingAtBoundary:AVSpeechBoundaryImmediate];
}

extern "C" void CareVoicePause(void)
{
  AVSpeechSynthesizer *synthesizer = CareVoiceSynthesizer();
  if (synthesizer.isSpeaking && !synthesizer.isPaused)
    [synthesizer pauseSpeakingAtBoundary:AVSpeechBoundaryWord];
}

extern "C" void CareVoiceResume(void)
{
  AVSpeechSynthesizer *synthesizer = CareVoiceSynthesizer();
  if (synthesizer.isPaused) [synthesizer continueSpeaking];
}
