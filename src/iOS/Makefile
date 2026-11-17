ARCHS = arm64
TARGET = iphone:clang:latest:12.0
include $(THEOS)/makefiles/common.mk
APPLICATION_NAME = BackThread
BackThread_FILES = UI/main.m UI/BTViewController.m Agent/BTAgent.m AudioReceiver/BTAudioReceiver.m AudioReceiver/BTPcmRing.c ../Shared/Protocol/bt_wire.c
BackThread_FRAMEWORKS = UIKit Foundation AVFoundation AudioToolbox MediaPlayer Security ImageIO
BackThread_CFLAGS = -fobjc-arc -Wall -Wextra
BackThread_INSTALL_PATH = /Applications
BackThread_RESOURCE_DIRS = Resources
include $(THEOS_MAKE_PATH)/application.mk
