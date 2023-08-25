// This resource is published under the Creative Commons Zero license.
// https://creativecommons.org/publicdomain/zero/1.0/
//
// 2018 HKU University of the Arts Utrecht, Niels Keetels
// Attibution of the author's name is appreciated but not required.

using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class Soundscape : MonoBehaviour {

	private const int sampleRate = 44100;
	private const float timeStep = 1.0f / sampleRate;
	private AudioClip clip = null;
	private AudioSource audioSource = null;
	public bool enableEchoes = true;
	public bool enableDistortion = true;
	public bool enableFilter = true;
	public bool randomness = true;
	public bool enableKS = true;
	public bool enableNoiseBurst = true;
	private float lowpass = 440.0f;
	private float resonance = 0.01f;
	private float globalFeedback = 0.7f;
	
	public class Foldback 
	{
		public float run(float sample, float threshold)
		{
			float s = sample;
			if (Mathf.Abs(sample) > threshold) 
			{
				s = Mathf.Abs(Mathf.Abs(Mathf.Repeat(sample - threshold, threshold * 4.0f)) - threshold * 2.0f) - threshold;
			}
			return s;
		}
	};

	
	public enum FilterType 
	{
		LOWPASS,
		HIGHPASS,
		NONE
	};

	// source of this algorithm?
	public class StateVariableFilter 
	{
		public float low = 0.0f;
		public float high = 0.0f;
		public float band = 0.0f;

		// 2-pole (12dB per octaaf) state variable filter
		public float Process(FilterType type, float sample, float cutoff, float q) {
			cutoff = 1.5f *  Mathf.Sin(cutoff * Mathf.PI / sampleRate);
			low += cutoff * band;
			high = q * sample - low - q * band;
			band = cutoff * high + band;

			float s = 0;
			switch (type) {
			case FilterType.LOWPASS:
				s = low;
				break;
			case FilterType.HIGHPASS:
				s = high;
				break;
			case FilterType.NONE:
				s = sample;
				break;
			}
			return s;
		}
	};	
	
	public class DelayLine
	{
		const int bufferSize = sampleRate * 8;
		
		private float[] l = new float[bufferSize];
		private float[] r = new float[bufferSize];

		private int delayLengthL = 1024;
		private int delayLengthR = 1024;
		
		public float feedback = 0.5f;
		public float crossFeedback = 0.25f;
		
		private int cursorL = 0;
		private int cursorR = 0;
		
		public void Set(int delayLengthL, int delayLengthR, float feedback, float crossFeedback)
		{
			this.delayLengthL = Mathf.Clamp(delayLengthL, 1, bufferSize);
			this.delayLengthR = Mathf.Clamp(delayLengthR, 1, bufferSize);
			this.feedback = Mathf.Clamp(feedback, 0.0f, 1.0f);
			this.crossFeedback = Mathf.Clamp(crossFeedback, 0.0f, 1.0f);			
		}
		
		public void Apply(float[] samples, float wet)
		{
			int index = 0;
			Vector2 sample = Vector2.zero;
			for (int i = 0; i < samples.Length/2; i++)
			{
				sample.x = l[cursorL] * feedback + r[cursorR] * crossFeedback + samples[index+0];
				sample.y = r[cursorR] * feedback + l[cursorL] * crossFeedback + samples[index+1];
				
				samples[index+0] += (sample.x - samples[index+0]) * wet;
				samples[index+1] += (sample.y - samples[index+1]) * wet;

				sample.x = Mathf.Clamp(sample.x, -1.0f, 1.0f);
				sample.y = Mathf.Clamp(sample.y, -1.0f, 1.0f);

				l[cursorL] = sample.x;
				r[cursorR] = sample.y;		
				
				cursorL = (cursorL + 1) % delayLengthL;
				cursorR = (cursorR + 1) & delayLengthR;
		
				index += 2;
			}			
		}
	};
	
	private static System.Random random = new System.Random();
	float GenerateSine(float phase, float frequency) 
	{
		return Mathf.Sin (frequency * phase * Mathf.PI * 2.0f);
	}
	
	float GenerateSawtooth(float phase, float frequency) 
	{
		return (phase * frequency - Mathf.Floor(0.5f + phase * frequency)) * 2.0f;
	}
	
	float GenerateTriangle(float phase, float frequency) 
	{
		return Mathf.Abs(GenerateSawtooth(phase, frequency)) * 2.0f - 1.0f;
	}
	
	float GenerateSquare(float phase, float frequency) 
	{
		return Mathf.Sin (frequency * phase * Mathf.PI * 2.0f) < 0 ? -1.0f : 1.0f;
	}
	
	float GenerateNoise(float phase, float frequency) 
	{
		return (float)random.Next(65536) / 32768.0f - 1.0f;
	}
	
	public class KS
	{
		const int maxBufferSize = 500;
		public float[] buffer = new float[maxBufferSize];
		int buffersize = 0;
		bool isInitialized = false;
		
		public void pluck(float frequency, int durationMs)
		{
			buffersize = (int)Mathf.Ceil(sampleRate / frequency);
			if (buffersize >= maxBufferSize)
				buffersize = maxBufferSize;
			
			for (int i = 0; i < buffersize; i++)
			{
				buffer[i] = (float)random.Next(65535) / 32768.0f - 1.0f;
			}		

			isInitialized = true;
		}
		
		int index = 0;
		public float sample()
		{
			if (!isInitialized)
				return 0.0f;
	
			float avg = 0.996f * .5f * (buffer[index] + buffer[(index+1) % buffersize]);			
			buffer[index] = avg;
			index++;
			index %= buffersize;
			return avg;
		}
	};
	
	int transpose = 0;
	float getFrequency(int midiNote)
	{
		return 440.0f * Mathf.Pow(2.0f, (midiNote + transpose - 69) / 12.0f);
	}	
		
	int curnote = 0;
	
	DelayLine delay1 = new DelayLine();
	DelayLine delay2 = new DelayLine();
	DelayLine delay3 = new DelayLine();	

	Foldback fold = new Foldback();
	
	StateVariableFilter svFilter = new StateVariableFilter();
	
	KS[] strings = new KS[6];
	
	int[] blues = {60, 63, 65, 66, 67, 70, 72, 70, 67, 66, 65, 63 };

	void Initialize()
	{
		audioSource = gameObject.AddComponent<AudioSource>();
		clip = AudioClip.Create("Synthesizer", 44100, 2, sampleRate, false, true, OnAudioFilterRead);

		audioSource.clip = clip;
		audioSource.loop = true;
		audioSource.Play();			
	}

	void Start () 
	{
		for (int i = 0; i < 6; i++)
		{
			strings[i] = new KS();
		}

		delay1.Set(19200, 25600, 0.55f, 0.25f);
		delay2.Set(25600,19200 , 0.55f, 0.25f);
		delay3.Set(23000,22000, 0.55f, 0.25f);	
	
		Initialize();
	}
		
	int curstring = 0;
	float pluckDeltaTimer = 0.0f;
	float clock = 0.0f;
	float noteClock = 0.0f;
	float triggerNoteOn = 1.0f;
	bool isPlaying = false;
	float freq = 220.0f;
	float noteLength = 0.1f;
	int waveform = 1;
	int burstCount = 0;
	float timeBetween = 2.9f;
	float attack = 0.0f;
	float noiseBurst = 0.0f;
	float nextNoiseBurst = 1.0f;
	float noiseBurstLength = 0.0015f;
	int noiseCount = 5;
	
	void OnAudioFilterRead(float[] samples) 
	{
		int index = 0;
		for (int i = 0; i < samples.Length / 2; i++) 
		{
			samples[index+0] = 0.0f;
			samples[index+1] = 0.0f;	
		
			noiseBurst += timeStep;
			clock += timeStep;
			noteClock += timeStep;
			pluckDeltaTimer += timeStep;
			
			if (noteClock >= triggerNoteOn)
			{				
				isPlaying = true;

				if (noteClock >= triggerNoteOn + noteLength*0.8f)
				{
					attack -= timeStep * 5.0f;
					if (attack <= 0.0001f)
						attack = 0.00001f;
				}	
				else
				if (attack < 0.9999f)
				{
						attack += timeStep * 2.0f;
				}
				attack = Mathf.Min(attack, 0.9999f);
			}	
			

			if (isPlaying && randomness)
			{
			float detune1 = freq * 0.009f;
				switch (waveform)
				{
					case 0:					
					samples[index+0] += GenerateSine(clock, freq + detune1) * 0.73f;
					samples[index+1] += GenerateSine(clock, freq - detune1) * 0.73f;
					break;
					case 1:					
					samples[index+0] += GenerateSawtooth(clock, freq + detune1) * 0.75f;
					samples[index+1] += GenerateSawtooth(clock, freq - detune1) * 0.75f;
					break;
					case 2:					
					samples[index+0] += GenerateTriangle(clock, freq + detune1) * 0.75f;
					samples[index+1] += GenerateTriangle(clock, freq - detune1) * 0.75f;
					break;
					case 3:					
					samples[index+0] += GenerateSquare(clock, freq + detune1) * 0.74f;
					samples[index+1] += GenerateSquare(clock, freq - detune1) * 0.74f;
					break;
					case 4:					
					samples[index+0] += GenerateNoise(clock, freq) * 0.74f;
					samples[index+1] += GenerateNoise(clock, freq) * 0.74f;
					break;
				}				

				if (noteClock >= triggerNoteOn + noteLength)
				{
					noteClock = 0.0f;
					
					triggerNoteOn = Mathf.Abs(GenerateNoise(0.0f, 0.0f) + 1.0f) * timeBetween; 
					noteLength =  Mathf.Abs(GenerateNoise(0.0f, 0.0f)) * 0.2f * timeBetween + 1.05f;
					burstCount++;
				
					freq = getFrequency(10 + (int)(GenerateNoise(0.0f, 0.0f) * 50 + 1)); 
					
					// C2
					freq = getFrequency(36);
					
					waveform++;
					if (waveform >= 4)
					waveform = 0;
					attack = 0.0f;
					
					isPlaying = false;
				}
				
				samples[index+0] = Mathf.Clamp(samples[index+0] * attack, -1.0f, 1.0f);			
				samples[index+1] = Mathf.Clamp(samples[index+1] * attack, -1.0f, 1.0f);					
			}
	
			// knetters
			if (enableNoiseBurst && (noiseBurst > nextNoiseBurst))
			{
				samples[index+0] += svFilter.Process(FilterType.LOWPASS, GenerateNoise(clock, freq) * 0.70f, 2000.0f + 1800.0f * Mathf.Sin(clock * 10.0f), 0.99f);
				samples[index+1] += svFilter.Process(FilterType.LOWPASS, GenerateNoise(clock, freq) * 0.70f, 2000.0f + 1800.0f * Mathf.Sin(clock * 10.0f), 0.99f);
				
				if (noiseBurst > nextNoiseBurst + noiseBurstLength)
				{
					noiseBurst = 0.0f;
					nextNoiseBurst = GenerateNoise(clock, freq)*0.3f+0.02f;
					noiseBurstLength = 0.0015f + (Mathf.Abs(GenerateNoise(clock, freq)*0.025f));
					
					noiseCount--;
				
					if (noiseCount <= 0)
					{
						nextNoiseBurst += 5 + GenerateNoise(clock, freq) * 3;
						noiseCount = 5;
					}
				}				
			}			

			if (enableKS && (pluckDeltaTimer >= 0.17f))
			{
				int note = random.Next(20) + 50;
				note = blues[curnote];
				
				if (((int)(clock)&11) == 0)
				{
					strings[curstring].pluck(getFrequency(note), 1000);
				}
					
				curstring++;
				curstring %= 6;
				pluckDeltaTimer = 0.0f;
				
				if ((curnote & 3) == 0)
					pluckDeltaTimer -= 1.0f;
				
				curnote++;
				curnote = random.Next(blues.Length);
				curnote %= blues.Length;
			}
			
			if (enableDistortion)
			{				
				samples[index+0] = fold.run(Mathf.Clamp(samples[index+1], 0.0f, 1.0f), 0.4f + 0.3f * Mathf.Sin(clock));			
				samples[index+1] = fold.run(Mathf.Clamp(samples[index+1], 0.0f, 1.0f), 0.4f + 0.3f * Mathf.Sin(clock));			
			}
			
			for (int numstrings = 0; numstrings < 6; numstrings++)
			{
				float ks = strings[numstrings].sample();
				samples[index+0] += ks * 0.5f;
				samples[index+1] += ks * 0.5f;
			}			
						
			index += 2;
		}
	
		delay1.Set(19200, 25600, 0.4f + 0.3f * Mathf.Abs(Mathf.Sin(clock * 0.3f)), 0.25f);
		delay2.Set(25600, 19200, 0.4f + 0.3f * Mathf.Abs(Mathf.Cos(clock * 0.3f)), 0.25f);
	
		if (enableEchoes)
		{
			delay1.Apply(samples, 0.25f);
			delay2.Apply(samples, 0.25f);		
		}
		
		
	}

}
