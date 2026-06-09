namespace StateSync.Protocol
{
    public readonly struct RotationSample
    {
        public readonly float YawDeg;
        public readonly float PitchDeg;
        public readonly float RollDeg;
        public readonly float SenderTimestamp;
        public readonly float ArrivalTime;

        public RotationSample(float yawDeg, float pitchDeg, float rollDeg, float senderTimestamp, float arrivalTime)
        {
            YawDeg = yawDeg;
            PitchDeg = pitchDeg;
            RollDeg = rollDeg;
            SenderTimestamp = senderTimestamp;
            ArrivalTime = arrivalTime;
        }
    }
}
