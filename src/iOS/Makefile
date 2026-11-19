ARCHS = arm64
ifeq ($(IOS15),1)
TARGET = iphone:clang:latest:15.0
else
TARGET = iphone:clang:latest:12.0
endif
include $(THEOS)/makefiles/common.mk
APPLICATION_NAME = BackThread
BackThread_FILES = UI/main.m UI/BTTheme.m UI/BTVideoSurfaceView.m UI/BTNowPlayingView.m UI/BTIdleHubView.m UI/BTQuickActionsView.m UI/BTWorkerStatusView.m UI/BTSettingsView.m UI/BTNotificationOverlayView.m UI/BTViewController.m Media/BTBufferedMediaEngine.m Media/BTDirectMediaPlayer.m Media/BTYouTubeEmbedController.m Agent/BTAgent.m Transport/BTWifiClient.m Worker/BTWorkerEngine.m AudioReceiver/BTAudioSessionCoordinator.m AudioReceiver/BTAudioReceiver.m AudioReceiver/BTPcmRing.c ../Shared/Protocol/bt_wire.c
BackThread_FRAMEWORKS = UIKit Foundation AVFoundation CoreMedia AudioToolbox MediaPlayer WebKit Security Network ImageIO CoreGraphics QuartzCore SystemConfiguration
BackThread_WEAK_FRAMEWORKS = PDFKit Vision Speech
BackThread_LIBRARIES = z
BackThread_CFLAGS = -Wall -Wextra -Werror -Werror=unguarded-availability-new
BackThread_OBJCFLAGS = -fobjc-arc -fblocks
BackThread_INSTALL_PATH = /Applications
BackThread_RESOURCE_DIRS = Resources
include $(THEOS_MAKE_PATH)/application.mk
