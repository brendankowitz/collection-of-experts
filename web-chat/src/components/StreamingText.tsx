import { useState, useEffect, useRef, memo } from 'react';

interface StreamingTextProps {
  text: string;
  isStreaming: boolean;
  speed?: number;
  onComplete?: () => void;
}

const StreamingText = memo(function StreamingText({
  text,
  isStreaming,
  speed = 30,
  onComplete,
}: StreamingTextProps) {
  const [displayedText, setDisplayedText] = useState('');
  const indexRef = useRef(0);
  const intervalRef = useRef<ReturnType<typeof setInterval> | null>(null);

  useEffect(() => {
    if (!isStreaming) {
      setDisplayedText(text);
      return;
    }

    // Reset if text changed significantly (new streaming session)
    if (text.length < displayedText.length) {
      indexRef.current = 0;
      setDisplayedText('');
    }

    // Clear existing interval
    if (intervalRef.current) {
      clearInterval(intervalRef.current);
    }

    // Animate characters from current position to full text
    intervalRef.current = setInterval(() => {
      indexRef.current = text.length;
      setDisplayedText(text);

      if (indexRef.current >= text.length) {
        if (intervalRef.current) {
          clearInterval(intervalRef.current);
        }
        onComplete?.();
      }
    }, speed);

    // Also update immediately for new content
    if (text.length > displayedText.length) {
      indexRef.current = text.length;
      setDisplayedText(text);
    }

    return () => {
      if (intervalRef.current) {
        clearInterval(intervalRef.current);
      }
    };
  }, [text, isStreaming, speed, onComplete]);

  // If streaming, show the full text (it's already being typed by the parent)
  const displayText = isStreaming ? text : displayedText;

  return (
    <span className="whitespace-pre-wrap">
      {displayText}
      {isStreaming && (
        <span className="inline-block w-0.5 h-5 ml-0.5 bg-blue-400 animate-typing-pulse align-middle" />
      )}
    </span>
  );
});

export default StreamingText;
