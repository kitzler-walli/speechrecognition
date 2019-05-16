using System;
using System.Diagnostics;
using System.Reactive.Linq;
using Android.App;
using Android.Content;
using Android.Speech;
using Plugin.Permissions;
using Plugin.Permissions.Abstractions;


namespace Plugin.SpeechRecognition
{
    public class SpeechRecognizerImpl : AbstractSpeechRecognizer
    {
        readonly object syncLock = new object();
        readonly IPermissions permissions;


        public SpeechRecognizerImpl(IPermissions permissions = null) => this.permissions = permissions ?? CrossPermissions.Current;


        public override bool IsSupported => SpeechRecognizer.IsRecognitionAvailable(Application.Context);
        public override IObservable<SpeechRecognizerStatus> RequestPermission() => Observable.FromAsync(async ct =>
        {
            if (!this.IsSupported)
                return SpeechRecognizerStatus.NotSupported;

            var status = await this.permissions.RequestPermissionsAsync(Permission.Speech);
            switch (status[Permission.Speech])
            {
                case PermissionStatus.Restricted:
                case PermissionStatus.Denied:
                    return SpeechRecognizerStatus.PermissionDenied;

                case PermissionStatus.Unknown:
                    return SpeechRecognizerStatus.NotSupported;

                case PermissionStatus.Disabled:
                    return SpeechRecognizerStatus.Disabled;

                default:
                    return SpeechRecognizerStatus.Available;
            }
        });


        public override IObservable<string> ListenUntilPause() => Observable.Create<string>(ob =>
        {
            var final = "";
            var listener = new SpeechRecognitionListener
            {
                ReadyForSpeech = () => this.ListenSubject.OnNext(true),
                Error = ex => ob.OnError(new Exception("Failure in speech engine - " + ex)),
                PartialResults = sentence =>
                {
                    lock (this.syncLock)
                        final = sentence;
                },
                FinalResults = sentence =>
                {
                    lock (this.syncLock)
                        final = sentence;
                },
                EndOfSpeech = () =>
                {
                    lock (this.syncLock)
                    {
                        ob.OnNext(final);
                        ob.OnCompleted();
                        this.ListenSubject.OnNext(false);
                    }
                }
            };
            var speechRecognizer = SpeechRecognizer.CreateSpeechRecognizer(Application.Context);
            speechRecognizer.SetRecognitionListener(listener);
            speechRecognizer.StartListening(this.CreateSpeechIntent(true));
            //speechRecognizer.StartListening(this.CreateSpeechIntent(false));

            return () =>
            {
                this.ListenSubject.OnNext(false);
                speechRecognizer.StopListening();
                speechRecognizer.Destroy();
            };
        });


        public override IObservable<string> ContinuousDictation() => Observable.Create<string>(ob =>
        {
            var stop = false;
            var currentIndex = 0;
            var speechRecognizer = SpeechRecognizer.CreateSpeechRecognizer(Application.Context);
            var listener = new SpeechRecognitionListener();
            string lastsentence = string.Empty;

            listener.ReadyForSpeech = () => this.ListenSubject.OnNext(true);
            listener.PartialResults = sentence =>
            {
                lock (this.syncLock)
                {
                    sentence = sentence.Trim();
                    if (sentence.Length > 0)
                    {
                        if (!string.IsNullOrEmpty(lastsentence) && sentence.IndexOf(lastsentence) == 0)
                            currentIndex = lastsentence.Length;
                        else if (!string.IsNullOrEmpty(lastsentence) && lastsentence.IndexOf(sentence) == 0)
                            return; // in this case we ignore the sentence
                        else if (currentIndex > sentence.Length)
                        {
                            currentIndex = 0;
                            lastsentence = string.Empty;
                        }

                        lastsentence = sentence;

                        var newPart = sentence.Substring(currentIndex).Trim();
                        currentIndex = sentence.Length;
                        foreach (var part in newPart.Split(' '))
                        {
                            var trimpart = part.Trim();
                            if(trimpart.Length > 0)
                            {
                                Debug.WriteLine("filtered: " + trimpart);
                                ob.OnNext(trimpart);
                            }
                        }
                    }
                }
            };

            listener.EndOfSpeech = () =>
            {
                lock (this.syncLock)
                {
                    currentIndex = 0;
                    speechRecognizer.Destroy();
                    speechRecognizer = null;

                    speechRecognizer = SpeechRecognizer.CreateSpeechRecognizer(Application.Context);
                    speechRecognizer.SetRecognitionListener(listener);
                    speechRecognizer.StartListening(this.CreateSpeechIntent(true));
                }
            };
            listener.Error = ex =>
            {
                switch (ex)
                {
                    case SpeechRecognizerError.Client:
                    case SpeechRecognizerError.RecognizerBusy:
                    case SpeechRecognizerError.SpeechTimeout:
                        lock (this.syncLock)
                        {
                            if (stop)
                                return;

                            speechRecognizer.Destroy();
                            speechRecognizer = null;

                            speechRecognizer = SpeechRecognizer.CreateSpeechRecognizer(Application.Context);
                            speechRecognizer.SetRecognitionListener(listener);
                            speechRecognizer.StartListening(this.CreateSpeechIntent(true));
                        }
                        break;

                    default:
                        ob.OnError(new Exception($"Could not start speech recognizer - ERROR: {ex}"));
                        break;
                }
            };
            speechRecognizer.SetRecognitionListener(listener);
            speechRecognizer.StartListening(this.CreateSpeechIntent(true));


            return () =>
            {
                stop = true;
                speechRecognizer?.StopListening();
                speechRecognizer?.Destroy();
                this.ListenSubject.OnNext(false);
            };
        });


        protected virtual Intent CreateSpeechIntent(bool partialResults)
        {
            var intent = new Intent(RecognizerIntent.ActionRecognizeSpeech);
            intent.PutExtra(RecognizerIntent.ExtraLanguagePreference, Java.Util.Locale.Default);
            intent.PutExtra(RecognizerIntent.ExtraLanguage, Java.Util.Locale.Default);
            intent.PutExtra(RecognizerIntent.ExtraLanguageModel, RecognizerIntent.LanguageModelFreeForm);
            intent.PutExtra(RecognizerIntent.ExtraCallingPackage, Application.Context.PackageName);
            //intent.PutExtra(RecognizerIntent.ExtraMaxResults, 1);
            //intent.PutExtra(RecognizerIntent.ExtraSpeechInputCompleteSilenceLengthMillis, 1500);
            //intent.PutExtra(RecognizerIntent.ExtraSpeechInputPossiblyCompleteSilenceLengthMillis, 1500);
            //intent.PutExtra(RecognizerIntent.ExtraSpeechInputMinimumLengthMillis, 15000);
            intent.PutExtra(RecognizerIntent.ExtraPartialResults, partialResults);

            return intent;
        }
    }
}


