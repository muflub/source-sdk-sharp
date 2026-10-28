//========= Copyright Valve Corporation, All rights reserved. ============//
// The copyright to the contents herein is the property of Valve, L.L.C.
// The contents may be used and/or copied only with the written permission of
// Valve, L.L.C., or in accordance with the terms and conditions stipulated in
// the agreement/contract under which the contents have been supplied.

#pragma once

#if defined ( WIN32 )
    #pragma once
#endif

#include <math.h>
#include "appframework/IAppSystem.h"
#include "tier0/platform.h"

#include <stdint.h>
#ifndef _STDINT_H
#define _STDINT_H
#endif
#ifndef _STDINT
#define _STDINT
#endif

#ifndef INT32_MAX
#define INT32_MAX    (0x7FFFFFFF)
#endif
#ifndef UINT32_MAX
#define UINT32_MAX   (0xFFFFFFFFu)
#endif

// Forward declarations
class IMaterial;

// Types used when dealing with video services

//#define ENABLE_EXTERNAL_ENCODER_LOGGING

// enums used when dealing with video services

	// various general video system enumerations

namespace VideoResult
{
};

namespace VideoSystem
{
};

namespace VideoSystemStatus
{
};

namespace VideoSystemFeature
{
	enum EVideoSystemFeature_t
	{
		NO_FEATURES					= 0x00000000,
		PLAY_VIDEO_FILE_FULL_SCREEN = 0x00000001,
		PLAY_VIDEO_FILE_IN_MATERIAL = 0x00000002,
		ENCODE_VIDEO_TO_FILE		= 0x00000004,
		ENCODE_AUDIO_TO_FILE		= 0x00000008,
		
		FULL_PLAYBACK				= PLAY_VIDEO_FILE_FULL_SCREEN | PLAY_VIDEO_FILE_IN_MATERIAL,
		FULL_ENCODE					= ENCODE_VIDEO_TO_FILE | ENCODE_AUDIO_TO_FILE,
		ALL_VALID_FEATURES			= FULL_PLAYBACK | FULL_ENCODE,
		
		ESF_FORCE_UINT32			= UINT32_MAX,
	};

	DEFINE_ENUM_BITWISE_OPERATORS( EVideoSystemFeature_t );
};
	
namespace VideoSoundDeviceOperation
{
};

	// Video Encoding related settings

namespace VideoEncodeCodec
{
	// NOTE: NEW CODECS SHOULD BE ADDED TO THE END OF THIS LIST.
	enum EVideoEncodeCodec_t
	{
		MPEG2_CODEC,
		MPEG4_CODEC,
		H261_CODEC,
		H263_CODEC,
		H264_CODEC,
		MJPEG_A_CODEC,
		MJPEG_B_CODEC,
		SORENSON3_CODEC,
		CINEPACK_CODEC,
		WEBM_CODEC,

		// NOTE: ADD NEW CODECS HERE.

		CODEC_COUNT,

		DEFAULT_CODEC = H264_CODEC,
	};
};
typedef VideoEncodeCodec::EVideoEncodeCodec_t VideoEncodeCodec_t;

namespace VideoEncodeQuality
{
};

namespace VideoEncodeSourceFormat
{
};

namespace VideoEncodeGamma
{
};

namespace VideoPlaybackGamma
{
};

	// Video Playback related settings

namespace VideoPlaybackFlags
{
	enum EVideoPlaybackFlags_t						// Options when playing a video file
	{
		NO_PLAYBACK_OPTIONS			= 0x00000000,
		
		// Full Screen Playback Options
		FILL_WINDOW					= 0x00000001,		// force video fill entire window
		LOCK_ASPECT_RATIO			= 0x00000002,		// preserve aspect ratio when scaling
		INTEGRAL_SCALE				= 0x00000004,		// scale video only by integral amounts
		CENTER_VIDEO_IN_WINDOW		= 0x00000008,		// center output video in window

		FORCE_MIN_PLAY_TIME			= 0x00000010,		// play for a minimum amount of time before allowing skip or abort
		
		ABORT_ON_SPACE				= 0x00000100,		// Keys to abort fullscreen playback on
		ABORT_ON_ESC				= 0x00000200,
		ABORT_ON_RETURN				= 0x00000400,
		ABORT_ON_ANY_KEY			= 0x00000700,
		
		PAUSE_ON_SPACE				= 0x00001000,		// Keys to pause fullscreen playback on
		PAUSE_ON_ESC				= 0x00002000,
		PAUSE_ON_RETURN				= 0x00004000,
		PAUSE_ON_ANY_KEY			= 0x00007000,
		
		LOOP_VIDEO					= 0x00010000,		// Full Screen and Video material
		NO_AUDIO					= 0x00020000,
		PRELOAD_VIDEO				= 0x00040000,
		
		DONT_AUTO_START_VIDEO		= 0x00100000,		// Don't begin playing until told to do so.
		TEXTURES_ACTUAL_SIZE		= 0x00200000,		// Try and use textures the same size as the video frame
		
		VALID_FULLSCREEN_FLAGS		= 0x0007771F,		// Playback Flags that are valid for playing videos fullscreen
		VALID_MATERIAL_FLAGS		= 0x00370000,		// Playback Flags that are valid for playing videos in a material

		DEFAULT_MATERIAL_OPTIONS	= NO_PLAYBACK_OPTIONS,
		DEFAULT_FULLSCREEN_OPTIONS	= CENTER_VIDEO_IN_WINDOW | LOCK_ASPECT_RATIO | ABORT_ON_ANY_KEY,
		
		EVPF_FORCE_UINT32			= UINT32_MAX,
	};
	
	DEFINE_ENUM_BITWISE_OPERATORS( EVideoPlaybackFlags_t );
}

namespace AudioEncodeSourceFormat
{
	enum EAudioEncodeSourceFormat_t				// Audio source format to encode
	{
		AUDIO_NONE = 0,
		AUDIO_16BIT_PCMStereo,
		
		AUDIO_FORMAT_COUNT
	};
};
typedef AudioEncodeSourceFormat::EAudioEncodeSourceFormat_t AudioEncodeSourceFormat_t;

namespace AudioEncodeOptions
{
	enum EAudioEncodeOptions_t						// Options to control audio encoding
	{
		NO_AUDIO_OPTIONS					= 0x00000000,
		
		USE_AUDIO_ENCODE_GROUP_SIZE			= 0x00000001,		// When adding to the video media, use fixed size sample groups
		GROUP_SIZE_IS_VIDEO_FRAME			= 0x00000002,		// use a group size equal to one video frame in duration
		LIMIT_AUDIO_TRACK_TO_VIDEO_DURATION = 0x00000004,		// Don't let the Audio Track exceed the video track in duration
		PAD_AUDIO_WITH_SILENCE				= 0x00000008,		// If Audio track duration is less than video track's, pad with silence
		
		AEO_FORCE_UINT32					= UINT32_MAX,
	};
	DEFINE_ENUM_BITWISE_OPERATORS( EAudioEncodeOptions_t );
}

// Frame Rate Class
class VideoFrameRate_t
{
	public:
		inline						VideoFrameRate_t() : m_TimeUnitsPerSecond( 0 ), m_TimeUnitsPerFrame( 1000 ) {};
		
		inline						VideoFrameRate_t( int FPS, bool NTSC )		{ SetFPS( FPS, NTSC); } 
		inline	explicit			VideoFrameRate_t( float FPS )				{ SetFPS( FPS); };

		inline VideoFrameRate_t&	operator=( const VideoFrameRate_t& rhs ) { m_TimeUnitsPerSecond = rhs.m_TimeUnitsPerSecond; m_TimeUnitsPerFrame = rhs.m_TimeUnitsPerFrame; return *this; }
		inline						VideoFrameRate_t( const VideoFrameRate_t &rhs ) { *this = rhs; };

		inline void					SetFPS( int FPS, bool NTSC )	{ m_TimeUnitsPerSecond = FPS * 1000; m_TimeUnitsPerFrame = 1000 + (uint) NTSC; }
		inline void					SetFPS( float FPS )				{ m_TimeUnitsPerSecond = (uint) ( FPS * 1000.0f ); m_TimeUnitsPerFrame = 1000; }
		
		inline void					Clear()							{ m_TimeUnitsPerSecond = 0; m_TimeUnitsPerFrame = 1000; }
		inline bool					IsValid()						{ return ( m_TimeUnitsPerSecond != 0); }

	private:
		uint32						m_TimeUnitsPerSecond;
		uint32						m_TimeUnitsPerFrame;
};

// specific interfaces returned and managed by video services

// Video Material interface - manages the playing back of a video to a 
//     a material / texture combo
class IVideoMaterial : public IBaseInterface
{
	public:
		// Video information functions		
		virtual void unused_GetVideoFileName() = 0;								// Gets the file name of the video this material is playing
		virtual void unused_GetLastResult() = 0;								// Gets detailed info on the last operation
		
		virtual void unused_GetVideoFrameRate() = 0;							// Returns the frame rate of the associated video in FPS

		// Audio Functions
		virtual void unused_HasAudio() = 0;										// Query if the video has an audio track
		
		virtual void unused_SetVolume() = 0;						// Adjust the playback volume
		virtual void unused_GetVolume() = 0;									// Query the current volume
		
		virtual void unused_SetMuted() = 0;					// Mute/UnMutes the audio playback
		virtual void unused_IsMuted() = 0;										// Query muted status

		virtual void unused_SoundDeviceCommand() = 0;		// Assign Sound Device for this Video Material
		
		// Video playback state functions
		virtual void unused_IsVideoReadyToPlay() = 0;							// Queries if the video material was initialized successfully and is ready for playback, but not playing or finished
		virtual void unused_IsVideoPlaying() = 0;								// Is the video currently playing (and needs update calls, etc), or paused while playing?
		virtual void unused_IsNewFrameReady() = 0;								// Do we have a new frame to get & display?
		virtual void unused_IsFinishedPlaying() = 0;							// Have we reached the end of the movie
		
		virtual void unused_StartVideo() = 0;									// Starts the video playing
		virtual void unused_StopVideo() = 0;									// Terminates the video playing
		
		virtual void unused_SetLooping() = 0;					// Sets the video to loop (or not)
		virtual void unused_IsLooping() = 0;									// Queries if the video is looping
		
		virtual void unused_SetPaused() = 0;					// Pauses or Unpauses video playback
		virtual bool				IsPaused() = 0;										// Queries if the video is paused

		// Position in playback functions
		virtual void unused_GetVideoDuration() = 0;								// Returns the duration of the associated video in seconds
		virtual void unused_GetFrameCount() = 0;								// Returns the total number of (unique) frames in the video
		
		virtual bool				SetFrame( int FrameNum ) = 0;						// Sets the current frame # in the video to play next 
		virtual void unused_GetCurrentFrame() = 0;								// Gets the current frame # for the video playback, 0 based
		
		virtual void unused_SetTime() = 0;						// Sets the video playback to specified time (in seconds)
		virtual void unused_GetCurrentVideoTime() = 0;							// Gets the current time in the video playback

		// Update function
		virtual bool				Update() = 0;										// Updates the video frame to reflect the time passed, true = new frame available

		// Material / Texture Info functions
		virtual IMaterial		   *GetMaterial() = 0;									// Gets the IMaterial associated with an video material

		virtual void unused_GetVideoTexCoordRange() = 0;		// Returns the max texture coordinate of the video portion of the material surface ( 0.0, 0.0 to U, V )
		virtual void unused_GetVideoImageSize() = 0;				// Returns the frame size of the Video Image Frame in pixels ( the stored in a subrect of the material itself)
		
};

// Video Recorder interface - manages the creation of a new video file
class IVideoRecorder : public IBaseInterface
{
	public:
		virtual bool				EstimateMovieFileSize( size_t *pEstSize, int movieWidth, int movieHeight, VideoFrameRate_t movieFps, float movieDuration, VideoEncodeCodec_t theCodec, int videoQuality,  AudioEncodeSourceFormat_t srcAudioFormat = AudioEncodeSourceFormat::AUDIO_NONE, int audioSampleRate = 0 ) = 0;
		
		virtual void unused_CreateNewMovieFile() = 0;
		
		virtual void unused_SetMovieVideoParameters() = 0;
		virtual void unused_SetMovieSourceImageParameters() = 0;
		virtual void unused_SetMovieSourceAudioParameters() = 0;
		
		virtual void unused_IsReadyToRecord() = 0;
		virtual void unused_GetLastResult() = 0;
		
		virtual void unused_AppendVideoFrame() = 0;
		virtual bool				AppendAudioSamples( void *pSampleBuffer, size_t sampleSize ) = 0;
		
		virtual void unused_GetFrameCount() = 0;
		virtual void unused_GetSampleCount() = 0;
		
		virtual void unused_GetFPS() = 0;
		virtual void unused_GetSampleRate() = 0;
		
		virtual void unused_AbortMovie() = 0;
		virtual void unused_FinishMovie() = 0;
		
#ifdef ENABLE_EXTERNAL_ENCODER_LOGGING
 		virtual bool                LogMessage(  const char *msg ) = 0;
#endif
		
};

// Main VIDEO_SERVICES interface
