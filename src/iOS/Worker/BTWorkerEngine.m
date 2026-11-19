#import "BTWorkerEngine.h"
#import <ImageIO/ImageIO.h>
#import <CoreGraphics/CoreGraphics.h>
#include <zlib.h>
#include <stdio.h>
#include <string.h>
#include <strings.h>
#include <sys/stat.h>

#if __has_include(<PDFKit/PDFKit.h>)
#import <PDFKit/PDFKit.h>
#define BT_HAS_PDFKIT 1
#else
#define BT_HAS_PDFKIT 0
#endif

#if __has_include(<Vision/Vision.h>)
#import <Vision/Vision.h>
#define BT_HAS_VISION 1
#else
#define BT_HAS_VISION 0
#endif

#if __has_include(<Speech/Speech.h>)
#import <Speech/Speech.h>
#define BT_HAS_SPEECH 1
#else
#define BT_HAS_SPEECH 0
#endif

static NSString *BTSafeOutputName(NSString *candidate, NSString *fallback) {
    NSString *name = ([candidate isKindOfClass:NSString.class] && candidate.length > 0 && candidate.length <= 120) ? candidate : fallback;
    if (!name.length || [name hasPrefix:@"."] || [name hasSuffix:@"."]) return nil;
    NSCharacterSet *bad = [[NSCharacterSet characterSetWithCharactersInString:@"abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789._-"] invertedSet];
    if ([name rangeOfCharacterFromSet:bad].location != NSNotFound) return nil;
    return name;
}

@implementation BTWorkerEngine

+ (NSArray<NSString *> *)supportedJobKinds {
    NSMutableArray<NSString *> *kinds = [@[
        @"sha256",
        @"verify",
        @"metadata",
        @"text",
        @"diagnostic",
        @"compress",
        @"decompress",
        @"log.scan",
        @"feed.poll",
        @"image.resize"
    ] mutableCopy];
#if BT_HAS_PDFKIT
    [kinds addObject:@"pdf.extract"];
#endif
    if (@available(iOS 15.0, *)) {
#if BT_HAS_VISION
        [kinds addObject:@"ocr.extract"];
#endif
#if BT_HAS_SPEECH
        [kinds addObject:@"speech.transcribe"];
#endif
    }
    return [kinds copy];
}

+ (BOOL)supportsExtendedJobKind:(NSString *)kind {
    return [@[@"compress", @"decompress", @"log.scan", @"feed.poll", @"image.resize", @"pdf.extract", @"ocr.extract", @"speech.transcribe"] containsObject:kind];
}

+ (NSString *)thermalStateName {
    switch ([NSProcessInfo processInfo].thermalState) {
        case NSProcessInfoThermalStateNominal: return @"nominal";
        case NSProcessInfoThermalStateFair: return @"fair";
        case NSProcessInfoThermalStateSerious: return @"serious";
        case NSProcessInfoThermalStateCritical: return @"critical";
    }
    return @"nominal";
}

+ (BOOL)isThermallyThrottled {
    NSProcessInfoThermalState state = [NSProcessInfo processInfo].thermalState;
    return state == NSProcessInfoThermalStateSerious || state == NSProcessInfoThermalStateCritical;
}

+ (nullable NSDictionary *)executeJobKind:(NSString *)kind
                                     name:(NSString *)name
                                inputPath:(nullable NSString *)inputPath
                               storageDir:(NSString *)storageDir
                               parameters:(nullable NSDictionary *)parameters
                              isCancelled:(BTWorkerCancelCheck)isCancelled
                               onProgress:(BTWorkerProgressBlock)onProgress
                                    error:(NSString * _Nullable * _Nullable)errorOut {
    NSDictionary *params = [parameters isKindOfClass:NSDictionary.class] ? parameters : @{};

    if ([self isThermallyThrottled] && [@[@"compress", @"image.resize", @"pdf.extract", @"ocr.extract", @"speech.transcribe"] containsObject:kind]) {
        if (errorOut) *errorOut = [NSString stringWithFormat:@"Deferred due to iPhone thermal state (%@)", [self thermalStateName]];
        return nil;
    }

    if ([kind isEqualToString:@"compress"]) {
        return [self runCompress:inputPath name:name storageDir:storageDir params:params isCancelled:isCancelled onProgress:onProgress error:errorOut];
    }
    if ([kind isEqualToString:@"decompress"]) {
        return [self runDecompress:inputPath name:name storageDir:storageDir params:params isCancelled:isCancelled onProgress:onProgress error:errorOut];
    }
    if ([kind isEqualToString:@"log.scan"]) {
        return [self runLogScan:inputPath params:params isCancelled:isCancelled onProgress:onProgress error:errorOut];
    }
    if ([kind isEqualToString:@"feed.poll"]) {
        return [self runFeedPoll:name params:params isCancelled:isCancelled error:errorOut];
    }
    if ([kind isEqualToString:@"image.resize"]) {
        return [self runImageResize:inputPath name:name storageDir:storageDir params:params isCancelled:isCancelled error:errorOut];
    }
    if ([kind isEqualToString:@"pdf.extract"]) {
        return [self runPdfExtract:inputPath name:name storageDir:storageDir isCancelled:isCancelled onProgress:onProgress error:errorOut];
    }
    if ([kind isEqualToString:@"ocr.extract"]) {
        return [self runOcrExtract:inputPath name:name storageDir:storageDir params:params isCancelled:isCancelled error:errorOut];
    }
    if ([kind isEqualToString:@"speech.transcribe"]) {
        return [self runSpeechTranscribe:inputPath params:params isCancelled:isCancelled error:errorOut];
    }

    if (errorOut) *errorOut = @"Unsupported extended job kind";
    return nil;
}

+ (nullable NSDictionary *)runCompress:(NSString *)inputPath
                                  name:(NSString *)name
                            storageDir:(NSString *)storageDir
                                params:(NSDictionary *)params
                           isCancelled:(BTWorkerCancelCheck)isCancelled
                            onProgress:(BTWorkerProgressBlock)onProgress
                                 error:(NSString **)errorOut {
    if (!inputPath.length) { if (errorOut) *errorOut = @"Invalid input path"; return nil; }
    NSString *outName = BTSafeOutputName(params[@"outputName"], [name stringByAppendingString:@".gz"]);
    if (!outName) { if (errorOut) *errorOut = @"Invalid output filename"; return nil; }
    NSString *outPath = [storageDir stringByAppendingPathComponent:outName];

    FILE *inFile = fopen(inputPath.fileSystemRepresentation, "rb");
    if (!inFile) { if (errorOut) *errorOut = @"Input file not found"; return nil; }
    struct stat st;
    uint64_t totalSize = (fstat(fileno(inFile), &st) == 0 && st.st_size > 0) ? (uint64_t)st.st_size : 0;

    FILE *outFile = fopen(outPath.fileSystemRepresentation, "wb");
    if (!outFile) { fclose(inFile); if (errorOut) *errorOut = @"Cannot create output archive"; return nil; }

    z_stream zs;
    memset(&zs, 0, sizeof(zs));
    int level = [params[@"level"] intValue];
    if (level < 1 || level > 9) level = Z_DEFAULT_COMPRESSION;
    if (deflateInit2(&zs, level, Z_DEFLATED, 16 + MAX_WBITS, 8, Z_DEFAULT_STRATEGY) != Z_OK) {
        fclose(inFile); fclose(outFile);
        if (errorOut) *errorOut = @"zlib deflateInit2 failed";
        return nil;
    }

    uint8_t inBuf[32768];
    uint8_t outBuf[32768];
    uint64_t inBytes = 0, outBytes = 0;
    double lastReported = 0;
    BOOL failed = NO;
    int flush = Z_NO_FLUSH;

    do {
        if (isCancelled()) { failed = YES; break; }
        size_t n = fread(inBuf, 1, sizeof(inBuf), inFile);
        if (ferror(inFile)) { failed = YES; break; }
        inBytes += n;
        flush = feof(inFile) ? Z_FINISH : Z_NO_FLUSH;
        zs.avail_in = (uInt)n;
        zs.next_in = inBuf;
        do {
            zs.avail_out = sizeof(outBuf);
            zs.next_out = outBuf;
            int ret = deflate(&zs, flush);
            if (ret == Z_STREAM_ERROR) { failed = YES; break; }
            size_t have = sizeof(outBuf) - zs.avail_out;
            if (have > 0 && fwrite(outBuf, 1, have, outFile) != have) { failed = YES; break; }
            outBytes += have;
        } while (zs.avail_out == 0 && !failed);
        if (totalSize > 0) {
            double p = (double)inBytes / (double)totalSize;
            if (p - lastReported >= 0.1) { onProgress(p); lastReported = p; }
        }
    } while (flush != Z_FINISH && !failed);

    deflateEnd(&zs);
    fclose(inFile);
    fclose(outFile);

    if (failed) {
        [[NSFileManager defaultManager] removeItemAtPath:outPath error:nil];
        if (errorOut && !isCancelled()) *errorOut = @"Gzip compression failed";
        return nil;
    }

    double ratio = inBytes > 0 ? (double)outBytes / (double)inBytes : 1.0;
    return @{
        @"outputName": outName,
        @"inputBytes": @(inBytes),
        @"outputBytes": @(outBytes),
        @"ratio": @(ratio),
        @"codec": @"gzip"
    };
}

+ (nullable NSDictionary *)runDecompress:(NSString *)inputPath
                                    name:(NSString *)name
                              storageDir:(NSString *)storageDir
                                  params:(NSDictionary *)params
                             isCancelled:(BTWorkerCancelCheck)isCancelled
                              onProgress:(BTWorkerProgressBlock)onProgress
                                   error:(NSString **)errorOut {
    if (!inputPath.length) { if (errorOut) *errorOut = @"Invalid input path"; return nil; }
    NSString *defaultOut = [name hasSuffix:@".gz"] && name.length > 3 ? [name substringToIndex:name.length - 3] : [name stringByAppendingString:@".out"];
    NSString *outName = BTSafeOutputName(params[@"outputName"], defaultOut);
    if (!outName) { if (errorOut) *errorOut = @"Invalid output filename"; return nil; }
    NSString *outPath = [storageDir stringByAppendingPathComponent:outName];

    FILE *inFile = fopen(inputPath.fileSystemRepresentation, "rb");
    if (!inFile) { if (errorOut) *errorOut = @"Input file not found"; return nil; }
    struct stat st;
    uint64_t totalSize = (fstat(fileno(inFile), &st) == 0 && st.st_size > 0) ? (uint64_t)st.st_size : 0;

    FILE *outFile = fopen(outPath.fileSystemRepresentation, "wb");
    if (!outFile) { fclose(inFile); if (errorOut) *errorOut = @"Cannot create output file"; return nil; }

    z_stream zs;
    memset(&zs, 0, sizeof(zs));
    if (inflateInit2(&zs, 32 + MAX_WBITS) != Z_OK) {
        fclose(inFile); fclose(outFile);
        if (errorOut) *errorOut = @"zlib inflateInit2 failed";
        return nil;
    }

    static const uint64_t kMaxDecompressedBytes = 256ULL * 1024ULL * 1024ULL;
    uint8_t inBuf[32768];
    uint8_t outBuf[32768];
    uint64_t inBytes = 0, outBytes = 0;
    double lastReported = 0;
    BOOL failed = NO;
    BOOL streamEnded = NO;

    while (!streamEnded && !failed) {
        if (isCancelled()) { failed = YES; break; }
        size_t n = fread(inBuf, 1, sizeof(inBuf), inFile);
        if (ferror(inFile)) { failed = YES; break; }
        if (n == 0) break;
        inBytes += n;
        zs.avail_in = (uInt)n;
        zs.next_in = inBuf;
        do {
            zs.avail_out = sizeof(outBuf);
            zs.next_out = outBuf;
            int ret = inflate(&zs, Z_NO_FLUSH);
            if (ret == Z_STREAM_ERROR || ret == Z_NEED_DICT || ret == Z_DATA_ERROR || ret == Z_MEM_ERROR) {
                failed = YES;
                break;
            }
            size_t have = sizeof(outBuf) - zs.avail_out;
            if (outBytes + have > kMaxDecompressedBytes) {
                if (errorOut) *errorOut = @"Decompressed size limit exceeded (256 MiB)";
                failed = YES;
                break;
            }
            if (have > 0 && fwrite(outBuf, 1, have, outFile) != have) { failed = YES; break; }
            outBytes += have;
            if (ret == Z_STREAM_END) { streamEnded = YES; break; }
        } while (zs.avail_out == 0 && !failed);
        if (totalSize > 0) {
            double p = (double)inBytes / (double)totalSize;
            if (p - lastReported >= 0.1) { onProgress(p); lastReported = p; }
        }
    }

    inflateEnd(&zs);
    fclose(inFile);
    fclose(outFile);

    if (failed || !streamEnded) {
        [[NSFileManager defaultManager] removeItemAtPath:outPath error:nil];
        if (errorOut && !*errorOut && !isCancelled()) *errorOut = @"Decompression failed or truncated archive";
        return nil;
    }

    return @{
        @"outputName": outName,
        @"inputBytes": @(inBytes),
        @"outputBytes": @(outBytes)
    };
}

+ (nullable NSDictionary *)runLogScan:(NSString *)inputPath
                               params:(NSDictionary *)params
                          isCancelled:(BTWorkerCancelCheck)isCancelled
                           onProgress:(BTWorkerProgressBlock)onProgress
                                error:(NSString **)errorOut {
    if (!inputPath.length) { if (errorOut) *errorOut = @"Invalid input path"; return nil; }
    FILE *file = fopen(inputPath.fileSystemRepresentation, "rb");
    if (!file) { if (errorOut) *errorOut = @"Log file not found"; return nil; }
    struct stat st;
    uint64_t totalSize = (fstat(fileno(file), &st) == 0 && st.st_size > 0) ? (uint64_t)st.st_size : 0;

    NSString *pattern = [params[@"pattern"] isKindOfClass:NSString.class] ? params[@"pattern"] : nil;
    const char *pat = pattern.length ? pattern.UTF8String : NULL;

    char lineBuf[4096];
    uint64_t bytes = 0, lines = 0, errors = 0, warnings = 0, matches = 0;
    NSMutableArray<NSString *> *samples = [NSMutableArray new];
    double lastReported = 0;

    while (fgets(lineBuf, sizeof(lineBuf), file) != NULL) {
        if (isCancelled()) { fclose(file); return nil; }
        size_t len = strlen(lineBuf);
        bytes += len;
        lines++;

        BOOL isErr = (strcasestr(lineBuf, "ERROR") != NULL || strcasestr(lineBuf, "FATAL") != NULL || strcasestr(lineBuf, "EXCEPTION") != NULL);
        BOOL isWarn = (!isErr && strcasestr(lineBuf, "WARN") != NULL);
        BOOL isMatch = (pat != NULL && strcasestr(lineBuf, pat) != NULL);

        if (isErr) errors++;
        if (isWarn) warnings++;
        if (isMatch) matches++;

        if ((isMatch || (pat == NULL && isErr)) && samples.count < 10) {
            NSString *s = [[NSString alloc] initWithBytes:lineBuf length:MIN(len, (size_t)200) encoding:NSUTF8StringEncoding];
            if (s) {
                [samples addObject:[s stringByTrimmingCharactersInSet:NSCharacterSet.newlineCharacterSet]];
            }
        }
        if (totalSize > 0) {
            double p = (double)bytes / (double)totalSize;
            if (p - lastReported >= 0.15) { onProgress(p); lastReported = p; }
        }
    }

    BOOL readErr = ferror(file) != 0;
    fclose(file);
    if (readErr) { if (errorOut) *errorOut = @"Log file read failed"; return nil; }

    return @{
        @"bytes": @(bytes),
        @"lines": @(lines),
        @"errors": @(errors),
        @"warnings": @(warnings),
        @"patternMatches": @(matches),
        @"samples": samples
    };
}

+ (nullable NSDictionary *)runFeedPoll:(NSString *)name
                                params:(NSDictionary *)params
                           isCancelled:(BTWorkerCancelCheck)isCancelled
                                 error:(NSString **)errorOut {
    NSString *urlStr = [params[@"url"] isKindOfClass:NSString.class] && [params[@"url"] length] ? params[@"url"] : name;
    NSURL *url = [NSURL URLWithString:urlStr ?: @""];
    if (!url || ![[url.scheme lowercaseString] isEqualToString:@"https"] || !url.host.length) {
        if (errorOut) *errorOut = @"Feed poll requires an https:// URL";
        return nil;
    }

    NSURLSessionConfiguration *cfg = [NSURLSessionConfiguration ephemeralSessionConfiguration];
    cfg.timeoutIntervalForRequest = 12.0;
    cfg.timeoutIntervalForResource = 15.0;
    NSURLSession *session = [NSURLSession sessionWithConfiguration:cfg];

    dispatch_semaphore_t sem = dispatch_semaphore_create(0);
    __block NSData *respData = nil;
    __block NSInteger statusCode = 0;
    __block NSError *netErr = nil;

    NSURLSessionDataTask *task = [session dataTaskWithURL:url completionHandler:^(NSData *data, NSURLResponse *response, NSError *err) {
        if ([response isKindOfClass:NSHTTPURLResponse.class]) {
            statusCode = ((NSHTTPURLResponse *)response).statusCode;
        }
        if (data.length > 262144) {
            respData = [data subdataWithRange:NSMakeRange(0, 262144)];
        } else {
            respData = data;
        }
        netErr = err;
        dispatch_semaphore_signal(sem);
    }];
    [task resume];

    for (int i = 0; i < 150; i++) {
        if (isCancelled()) {
            [task cancel];
            [session invalidateAndCancel];
            return nil;
        }
        if (dispatch_semaphore_wait(sem, dispatch_time(DISPATCH_TIME_NOW, (int64_t)(100 * NSEC_PER_MSEC))) == 0) break;
    }
    [session finishTasksAndInvalidate];

    if (netErr || !respData) {
        if (errorOut) *errorOut = netErr.localizedDescription ?: @"Feed request failed";
        return nil;
    }

    NSString *xml = [[NSString alloc] initWithData:respData encoding:NSUTF8StringEncoding] ?: @"";
    NSMutableArray<NSString *> *titles = [NSMutableArray new];
    NSRegularExpression *re = [NSRegularExpression regularExpressionWithPattern:@"<title[^>]*>(?:<!\\[CDATA\\[)?([^<]+?)(?:\\]\\]>)?</title>" options:NSRegularExpressionCaseInsensitive error:nil];
    NSArray<NSTextCheckingResult *> *matches = [re matchesInString:xml options:0 range:NSMakeRange(0, xml.length)];
    for (NSTextCheckingResult *m in matches) {
        if (titles.count >= 20) break;
        if (m.numberOfRanges > 1) {
            NSString *t = [[xml substringWithRange:[m rangeAtIndex:1]] stringByTrimmingCharactersInSet:NSCharacterSet.whitespaceAndNewlineCharacterSet];
            if (t.length) [titles addObject:t];
        }
    }

    return @{
        @"url": url.absoluteString,
        @"statusCode": @(statusCode),
        @"bytes": @(respData.length),
        @"titles": titles
    };
}

+ (nullable NSDictionary *)runImageResize:(NSString *)inputPath
                                     name:(NSString *)name
                               storageDir:(NSString *)storageDir
                                   params:(NSDictionary *)params
                              isCancelled:(BTWorkerCancelCheck)isCancelled
                                    error:(NSString **)errorOut {
    if (!inputPath.length || isCancelled()) { if (errorOut) *errorOut = @"Invalid image path"; return nil; }
    int maxPixel = [params[@"maxPixel"] intValue];
    if (maxPixel < 64 || maxPixel > 4096) maxPixel = 1280;
    double quality = [params[@"quality"] isKindOfClass:NSNumber.class] ? [params[@"quality"] doubleValue] : 0.85;
    if (quality < 0.1 || quality > 1.0) quality = 0.85;

    NSString *defaultOut = [[name stringByDeletingPathExtension] stringByAppendingFormat:@"_%d.jpg", maxPixel];
    NSString *outName = BTSafeOutputName(params[@"outputName"], defaultOut);
    if (!outName) { if (errorOut) *errorOut = @"Invalid output filename"; return nil; }
    NSString *outPath = [storageDir stringByAppendingPathComponent:outName];

    NSURL *inURL = [NSURL fileURLWithPath:inputPath];
    CGImageSourceRef src = CGImageSourceCreateWithURL((__bridge CFURLRef)inURL, NULL);
    if (!src) { if (errorOut) *errorOut = @"Unable to open source image"; return nil; }

    NSDictionary *thumbOpts = @{
        (id)kCGImageSourceCreateThumbnailFromImageAlways: @YES,
        (id)kCGImageSourceCreateThumbnailWithTransform: @YES,
        (id)kCGImageSourceThumbnailMaxPixelSize: @(maxPixel)
    };
    CGImageRef thumb = CGImageSourceCreateThumbnailAtIndex(src, 0, (__bridge CFDictionaryRef)thumbOpts);
    CFRelease(src);
    if (!thumb) { if (errorOut) *errorOut = @"Image thumbnail decode failed"; return nil; }

    size_t w = CGImageGetWidth(thumb);
    size_t h = CGImageGetHeight(thumb);
    NSURL *outURL = [NSURL fileURLWithPath:outPath];
    CGImageDestinationRef dst = CGImageDestinationCreateWithURL((__bridge CFURLRef)outURL, CFSTR("public.jpeg"), 1, NULL);
    if (!dst) {
        CGImageRelease(thumb);
        if (errorOut) *errorOut = @"Cannot create JPEG destination";
        return nil;
    }
    NSDictionary *destOpts = @{(id)kCGImageDestinationLossyCompressionQuality: @(quality)};
    CGImageDestinationAddImage(dst, thumb, (__bridge CFDictionaryRef)destOpts);
    BOOL ok = CGImageDestinationFinalize(dst);
    CFRelease(dst);
    CGImageRelease(thumb);

    if (!ok) {
        [[NSFileManager defaultManager] removeItemAtPath:outPath error:nil];
        if (errorOut) *errorOut = @"Failed to encode resized JPEG";
        return nil;
    }

    NSDictionary *attrs = [[NSFileManager defaultManager] attributesOfItemAtPath:outPath error:nil];
    return @{
        @"outputName": outName,
        @"width": @(w),
        @"height": @(h),
        @"outputBytes": attrs[NSFileSize] ?: @0
    };
}

+ (nullable NSDictionary *)runPdfExtract:(NSString *)inputPath
                                    name:(NSString *)name
                              storageDir:(NSString *)storageDir
                             isCancelled:(BTWorkerCancelCheck)isCancelled
                              onProgress:(BTWorkerProgressBlock)onProgress
                                   error:(NSString **)errorOut {
#if BT_HAS_PDFKIT
    if (!inputPath.length) { if (errorOut) *errorOut = @"Invalid PDF path"; return nil; }
    PDFDocument *doc = [[PDFDocument alloc] initWithURL:[NSURL fileURLWithPath:inputPath]];
    if (!doc) { if (errorOut) *errorOut = @"Could not open PDF document"; return nil; }

    NSUInteger pageCount = doc.pageCount;
    NSUInteger limit = MIN(pageCount, (NSUInteger)200);
    NSMutableString *collected = [NSMutableString new];
    static const NSUInteger kMaxChars = 512 * 1024;

    for (NSUInteger i = 0; i < limit; i++) {
        if (isCancelled()) return nil;
        @autoreleasepool {
            PDFPage *page = [doc pageAtIndex:i];
            NSString *txt = page.string;
            if (txt.length) {
                if (collected.length + txt.length > kMaxChars) {
                    NSUInteger take = kMaxChars - collected.length;
                    [collected appendString:[txt substringToIndex:take]];
                    break;
                }
                [collected appendString:txt];
                [collected appendString:@"\n"];
            }
        }
        if (limit > 0 && (i % 5 == 0)) onProgress((double)(i + 1) / (double)limit);
    }

    NSString *outName = BTSafeOutputName(nil, [[name stringByDeletingPathExtension] stringByAppendingString:@".txt"]);
    if (outName) {
        NSString *outPath = [storageDir stringByAppendingPathComponent:outName];
        [collected writeToFile:outPath atomically:YES encoding:NSUTF8StringEncoding error:nil];
    }

    NSString *preview = collected.length > 4096 ? [collected substringToIndex:4096] : [collected copy];
    return @{
        @"pageCount": @(pageCount),
        @"extractedPages": @(limit),
        @"charCount": @(collected.length),
        @"outputName": outName ?: @"",
        @"preview": preview
    };
#else
    (void)inputPath; (void)name; (void)storageDir; (void)isCancelled; (void)onProgress;
    if (errorOut) *errorOut = @"PDFKit unavailable in this build";
    return nil;
#endif
}

+ (nullable NSDictionary *)runOcrExtract:(NSString *)inputPath
                                    name:(NSString *)name
                              storageDir:(NSString *)storageDir
                                  params:(NSDictionary *)params
                             isCancelled:(BTWorkerCancelCheck)isCancelled
                                   error:(NSString **)errorOut {
#if BT_HAS_VISION
    if (@available(iOS 15.0, *)) {
        if (!inputPath.length || isCancelled()) { if (errorOut) *errorOut = @"Invalid image path for OCR"; return nil; }
        NSURL *url = [NSURL fileURLWithPath:inputPath];
        VNImageRequestHandler *handler = [[VNImageRequestHandler alloc] initWithURL:url options:@{}];
        NSMutableArray<NSString *> *lines = [NSMutableArray new];
        __block NSError *ocrError = nil;

        VNRecognizeTextRequest *req = [[VNRecognizeTextRequest alloc] initWithCompletionHandler:^(VNRequest *request, NSError *err) {
            if (err) { ocrError = err; return; }
            for (VNRecognizedTextObservation *obs in request.results) {
                VNRecognizedText *top = [[obs topCandidates:1] firstObject];
                if (top.string.length) [lines addObject:top.string];
            }
        }];
        req.recognitionLevel = VNRequestTextRecognitionLevelAccurate;
        req.usesLanguageCorrection = YES;
        if ([params[@"languages"] isKindOfClass:NSArray.class] && [params[@"languages"] count] > 0) {
            req.recognitionLanguages = params[@"languages"];
        }

        NSError *performErr = nil;
        BOOL ok = [handler performRequests:@[req] error:&performErr];
        if (!ok || ocrError) {
            if (errorOut) *errorOut = (ocrError ?: performErr).localizedDescription ?: @"Vision OCR failed";
            return nil;
        }

        NSString *fullText = [lines componentsJoinedByString:@"\n"];
        NSString *outName = BTSafeOutputName(params[@"outputName"], [[name stringByDeletingPathExtension] stringByAppendingString:@"_ocr.txt"]);
        if (outName) {
            [fullText writeToFile:[storageDir stringByAppendingPathComponent:outName] atomically:YES encoding:NSUTF8StringEncoding error:nil];
        }
        NSString *preview = fullText.length > 8192 ? [fullText substringToIndex:8192] : fullText;
        return @{
            @"text": preview,
            @"lineCount": @(lines.count),
            @"charCount": @(fullText.length),
            @"outputName": outName ?: @"",
            @"engine": @"Vision-ANE"
        };
    }
#endif
    (void)inputPath; (void)name; (void)storageDir; (void)params; (void)isCancelled;
    if (errorOut) *errorOut = @"OCR requires iOS 15.0 or newer";
    return nil;
}

+ (nullable NSDictionary *)runSpeechTranscribe:(NSString *)inputPath
                                        params:(NSDictionary *)params
                                   isCancelled:(BTWorkerCancelCheck)isCancelled
                                         error:(NSString **)errorOut {
#if BT_HAS_SPEECH
    if (@available(iOS 15.0, *)) {
        if (!inputPath.length || isCancelled()) { if (errorOut) *errorOut = @"Invalid audio path"; return nil; }
        if ([SFSpeechRecognizer authorizationStatus] != SFSpeechRecognizerAuthorizationStatusAuthorized) {
            if (errorOut) *errorOut = @"Speech recognition permission not granted on iPhone";
            return nil;
        }
        NSString *localeId = [params[@"locale"] isKindOfClass:NSString.class] && [params[@"locale"] length] ? params[@"locale"] : @"en-US";
        SFSpeechRecognizer *recognizer = [[SFSpeechRecognizer alloc] initWithLocale:[NSLocale localeWithLocaleIdentifier:localeId]];
        if (!recognizer || !recognizer.isAvailable || !recognizer.supportsOnDeviceRecognition) {
            if (errorOut) *errorOut = @"On-device speech recognition is unavailable for this locale";
            return nil;
        }

        SFSpeechURLRecognitionRequest *req = [[SFSpeechURLRecognitionRequest alloc] initWithURL:[NSURL fileURLWithPath:inputPath]];
        req.requiresOnDeviceRecognition = YES;
        req.shouldReportPartialResults = NO;

        dispatch_semaphore_t sem = dispatch_semaphore_create(0);
        __block NSString *transcript = nil;
        __block NSError *speechErr = nil;

        SFSpeechRecognitionTask *task = [recognizer recognitionTaskWithRequest:req resultHandler:^(SFSpeechRecognitionResult *result, NSError *err) {
            if (err) {
                speechErr = err;
                dispatch_semaphore_signal(sem);
                return;
            }
            if (result.isFinal) {
                transcript = result.bestTranscription.formattedString ?: @"";
                dispatch_semaphore_signal(sem);
            }
        }];

        for (int i = 0; i < 600; i++) {
            if (isCancelled()) {
                [task cancel];
                return nil;
            }
            if (dispatch_semaphore_wait(sem, dispatch_time(DISPATCH_TIME_NOW, (int64_t)(100 * NSEC_PER_MSEC))) == 0) break;
        }

        if (speechErr || transcript == nil) {
            [task cancel];
            if (errorOut) *errorOut = speechErr.localizedDescription ?: @"On-device speech transcription timed out";
            return nil;
        }

        return @{
            @"text": transcript,
            @"locale": localeId,
            @"onDevice": @YES
        };
    }
#endif
    (void)inputPath; (void)params; (void)isCancelled;
    if (errorOut) *errorOut = @"On-device speech transcription requires iOS 15.0 or newer";
    return nil;
}

@end
