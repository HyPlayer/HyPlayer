#include "signalsmith-stretch/signalsmith-stretch.h"

#include <cstdint>
#include <vector>

#if defined(_WIN32)
#define HYP_EXPORT extern "C" __declspec(dllexport)
#else
#define HYP_EXPORT extern "C" __attribute__((visibility("default")))
#endif

namespace {

using Stretch = signalsmith::stretch::SignalsmithStretch<float>;

struct Planar {
    std::vector<std::vector<float>> channels;
    std::vector<float*> pointers;

    void resize(int channelCount, int frames) {
        channels.resize(channelCount);
        pointers.resize(channelCount);
        for (int c = 0; c < channelCount; ++c) {
            if (static_cast<int>(channels[c].size()) < frames) channels[c].resize(frames);
            pointers[c] = channels[c].data();
        }
    }

    float* operator[](int c) { return pointers[c]; }
};

struct Instance {
    Stretch stretch;
    int channels = 2;
    Planar input;
    Planar output;

    void load(const float* interleaved, int frames) {
        input.resize(channels, frames);
        for (int i = 0; i < frames; ++i)
            for (int c = 0; c < channels; ++c)
                input.channels[c][i] = interleaved[i * channels + c];
    }

    void store(float* interleaved, int frames) {
        for (int i = 0; i < frames; ++i)
            for (int c = 0; c < channels; ++c)
                interleaved[i * channels + c] = output.channels[c][i];
    }
};

}

HYP_EXPORT void* hyp_stretch_create(int32_t channels, float sampleRate, int32_t cheaper)
{
    auto* instance = new Instance();
    instance->channels = channels;
    if (cheaper) instance->stretch.presetCheaper(channels, sampleRate);
    else instance->stretch.presetDefault(channels, sampleRate);
    return instance;
}

HYP_EXPORT void hyp_stretch_destroy(void* handle)
{
    delete static_cast<Instance*>(handle);
}

HYP_EXPORT void hyp_stretch_reset(void* handle)
{
    static_cast<Instance*>(handle)->stretch.reset();
}

HYP_EXPORT int32_t hyp_stretch_input_latency(void* handle)
{
    return static_cast<Instance*>(handle)->stretch.inputLatency();
}

HYP_EXPORT int32_t hyp_stretch_output_latency(void* handle)
{
    return static_cast<Instance*>(handle)->stretch.outputLatency();
}

HYP_EXPORT int32_t hyp_stretch_output_seek_length(void* handle, float playbackRate)
{
    return static_cast<Instance*>(handle)->stretch.outputSeekLength(playbackRate);
}

HYP_EXPORT void hyp_stretch_output_seek(void* handle, const float* interleaved, int32_t frames)
{
    auto* instance = static_cast<Instance*>(handle);
    instance->load(interleaved, frames);
    instance->stretch.outputSeek(instance->input, frames);
}

HYP_EXPORT void hyp_stretch_process(void* handle, const float* input, int32_t inputFrames, float* output, int32_t outputFrames)
{
    auto* instance = static_cast<Instance*>(handle);
    instance->load(input, inputFrames);
    instance->output.resize(instance->channels, outputFrames);
    instance->stretch.process(instance->input, inputFrames, instance->output, outputFrames);
    instance->store(output, outputFrames);
}

HYP_EXPORT void hyp_stretch_set_transpose(void* handle, float factor)
{
    static_cast<Instance*>(handle)->stretch.setTransposeFactor(factor);
}
