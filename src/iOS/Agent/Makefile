ARCHS = arm64
TARGET = iphone:clang:latest:12.0
include $(THEOS)/makefiles/common.mk
TOOL_NAME = BackThread.Agent
BackThread.Agent_FILES = main.m BTAgent.m $(wildcard ../Transport/*.m) ../AudioReceiver/BTAudioReceiver.m ../AudioReceiver/BTPcmRing.c ../../Shared/Protocol/bt_wire.c
BackThread.Agent_FRAMEWORKS = Foundation AVFoundation AudioToolbox Security
BackThread.Agent_CFLAGS = -Wall -Wextra -Werror -Werror=unguarded-availability-new
BackThread.Agent_OBJCFLAGS = -fobjc-arc -fblocks
BackThread.Agent_INSTALL_PATH = /var/mobile/BackThread/bin
include $(THEOS_MAKE_PATH)/tool.mk
