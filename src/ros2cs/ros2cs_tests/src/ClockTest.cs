// Copyright 2019-2021 Robotec.ai
// Copyright 2019 Dyno Robotics (by Samuel Lindgren samuel@dynorobotics.se)
// Modifications Copyright (c) 2026 Jianbin Liu.
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
// You may obtain a copy of the License at
//
//    http://www.apache.org/licenses/LICENSE-2.0
//
// Unless required by applicable law or agreed to in writing, software
// distributed under the License is distributed on an "AS IS" BASIS,
// WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
// See the License for the specific language governing permissions and
// limitations under the License.

// Modifications by Jianbin Liu:
// - Added negative nanosecond normalization and overflow coverage.
// - Split pure RosTime conversion tests away from ROS-initialized Clock tests.

using System;
using NUnit.Framework;

namespace ROS2.Test
{
    [TestFixture]
    public class ClockTest
    {
        [SetUp]
        public void SetUp()
        {
            Ros2cs.Init();
        }

        [TearDown]
        public void TearDown()
        {
            if (Ros2cs.Ok())
            {
                Ros2cs.Shutdown();
            }
        }

        [Test]
        public void CreateClock()
        {
            using var clock = new Clock();
            Assert.That(clock.IsDisposed, Is.False);
        }

        [Test]
        public void ClockGetNow()
        {
            using var clock = new Clock();
            RosTime timeNow = clock.Now;
            Assert.That(timeNow.sec, Is.GreaterThan(0));
        }
    }

    [TestFixture]
    public class RosTimeTest
    {
        [Test]
        public void RosTimeSeconds()
        {
            RosTime oneSecond = new RosTime { sec = 1, nanosec = 0 };
            Assert.That(oneSecond.Seconds, Is.EqualTo(1.0d));

            RosTime twoPointSix = new RosTime { sec = 2, nanosec = 600000000 };
            Assert.That(twoPointSix.Seconds, Is.EqualTo(2.6d));
        }

        /// <summary>Negative nanoseconds should normalize to the previous second plus positive nanoseconds.</summary>
        [Test]
        public void RosTimeFromNegativeNanosecondsIsNormalized()
        {
            RosTime time = Clock.FromNanoseconds(-1);

            Assert.That(time.sec, Is.EqualTo(-1));
            Assert.That(time.nanosec, Is.EqualTo(999999999));
        }

        /// <summary>builtin_interfaces/msg/Time stores seconds as int32, so overflow must fail explicitly.</summary>
        [Test]
        public void RosTimeOverflowThrows()
        {
            long overflowingNanoseconds = ((long)int.MaxValue + 1L) * 1000000000L;

            Assert.Throws<OverflowException>(() => Clock.FromNanoseconds(overflowingNanoseconds));
        }
    }
}
