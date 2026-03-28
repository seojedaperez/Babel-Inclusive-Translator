import sign_language_translator as slt

def main():
    try:
        model = slt.models.ConcatenativeSynthesis(
            text_language="en", sign_language="pk-sl", sign_format="video"
        )
        text = "hello world"
        sign = model.translate(text)
        sign.save("test_video.mp4")
        print("Generated video successfully: test_video.mp4")
    except Exception as e:
        print(f"Error: {e}")

if __name__ == '__main__':
    main()
